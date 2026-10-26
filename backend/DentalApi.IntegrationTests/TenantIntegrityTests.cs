using System.Text.Json;
using DentalApi.Data;
using DentalApi.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace DentalApi.IntegrationTests;

public sealed class TenantIntegrityTests
{
    private const string TenantMigration = "20261026100808_AddPatientUserTenantIntegrity";
    private const string PreviousMigration = "20261026074904_AddUserTokenVersion";

    // These tests apply actual migrations, rather than EnsureCreated.
    private sealed class Database : IAsyncDisposable
    {
        public SqliteConnection Connection { get; } = new("Data Source=:memory:;Foreign Keys=True");
        public AppDbContext Db { get; }
        public Database() => Db = new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(Connection).Options);

        public async Task InitializeAsync(bool upgrade = true)
        {
            await Connection.OpenAsync();
            await Db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            var created = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            await Db.Database.ExecuteSqlRawAsync("INSERT INTO Clinics (Id, Name, Phone, WorkHours) VALUES (101, 'Clinic A', '11111111', 'Synthetic hours'), (202, 'Clinic B', NULL, NULL)");
            Db.Users.AddRange(
                new User { Id = 11, ClinicId = 101, FullName = "Synthetic A", Email = "a@example.invalid", Phone = "11111111", Role = Role.Patient, PasswordHash = "synthetic-hash-A", CreatedAt = created, TokenVersion = 3, IsEmailVerified = true, IsPhoneVerified = true },
                new User { Id = 12, ClinicId = 202, FullName = "Synthetic B", Email = "b@example.invalid", Role = Role.Patient, PasswordHash = "synthetic-hash-B", CreatedAt = null });
            Db.Patients.Add(new Patient
            {
                Id = 21, ClinicId = 101, UserId = 11, FullName = "Synthetic patient A", Phone = "11111111",
                DateOfBirth = created.AddYears(-20), Address = "Synthetic address", Allergies = "Synthetic allergy",
                MedicalNotes = "Synthetic note", CreatedAt = created, IsActive = false
            });
            Db.Services.Add(new Service { Id = 31, ClinicId = 202, Name = "Synthetic service", Description = "Synthetic", ImagePath = "/synthetic.png", MinPrice = 100, MaxPrice = 200, AftercareText = "Synthetic aftercare" });
            Db.VerificationCodes.Add(new VerificationCode
            {
                Id = 41, UserId = 11, Channel = "email", Purpose = "password-reset", CodeHash = "synthetic-code-hash",
                CreatedAt = created, ExpiresAt = created.AddMinutes(2), FailedAttempts = 2, IsUsed = true
            });
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
            if (upgrade) await Db.GetService<IMigrator>().MigrateAsync(TenantMigration);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }

    [Theory]
    [InlineData(202, 12)]
    [InlineData(101, null)]
    public async Task Matching_tenant_and_unlinked_patient_are_allowed(int clinicId, int? userId)
    {
        await using var database = new Database();
        await database.InitializeAsync();
        database.Db.Patients.Add(new Patient { ClinicId = clinicId, UserId = userId, FullName = "Synthetic new patient", Phone = "33333333" });
        await database.Db.SaveChangesAsync();
        Assert.Equal(2, await database.Db.Patients.CountAsync());
        Assert.True(await database.Db.Patients.AnyAsync(p => p.ClinicId == clinicId && p.UserId == userId));
    }

    [Theory]
    [InlineData(12)] // Existing user in Clinic B.
    [InlineData(999)] // No such user.
    public async Task Invalid_user_link_fails_with_foreign_key_error(int userId)
    {
        await using var database = new Database();
        await database.InitializeAsync();
        var before = await SnapshotAsync(database.Db);
        database.Db.Patients.Add(new Patient { ClinicId = 101, UserId = userId, FullName = "Rejected", Phone = "33333333" });
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => database.Db.SaveChangesAsync());
        Assert.Equal(787, Assert.IsType<SqliteException>(error.InnerException).SqliteExtendedErrorCode);
        database.Db.ChangeTracker.Clear();
        Assert.Equal(before, await SnapshotAsync(database.Db));
    }

    [Theory]
    [InlineData("UPDATE Patients SET ClinicId = 202 WHERE Id = 21")]
    [InlineData("UPDATE Patients SET UserId = 12 WHERE Id = 21")]
    [InlineData("UPDATE Users SET ClinicId = 202 WHERE Id = 11")]
    [InlineData("DELETE FROM Users WHERE Id = 11")]
    public async Task Raw_sql_cannot_break_existing_link(string sql)
    {
        await using var database = new Database();
        await database.InitializeAsync();
        var before = await SnapshotAsync(database.Db);
        using var command = database.Connection.CreateCommand();
        command.CommandText = sql;
        var error = await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync());
        // RESTRICT can be SQLITE_CONSTRAINT_TRIGGER (1811); other FK failures are 787.
        Assert.Contains(error.SqliteExtendedErrorCode, new[] { 787, 1811 });
        Assert.Equal(before, await SnapshotAsync(database.Db));
    }

    [Fact]
    public async Task Upgrade_preserves_all_data_columns_indexes_and_other_foreign_keys()
    {
        await using var database = new Database();
        await database.InitializeAsync(upgrade: false);
        var before = await SnapshotAsync(database.Db);
        var tables = new[] { "Clinics", "Users", "Patients", "Services", "VerificationCodes" };
        var columns = new Dictionary<string, string>();
        foreach (var table in tables)
            columns[table] = await RowsAsync(database.Connection, $"PRAGMA table_info('{table}')");
        var indexes = await RowsAsync(database.Connection,
            "SELECT name, sql FROM sqlite_master WHERE type = 'index' AND name LIKE 'IX_%' ORDER BY name");
        var otherForeignKey = await RowsAsync(database.Connection, "PRAGMA foreign_key_list('VerificationCodes')");

        await database.Db.GetService<IMigrator>().MigrateAsync(TenantMigration);

        Assert.Equal(before, await SnapshotAsync(database.Db));
        foreach (var table in tables)
            // Column order can change during rebuild; compare metadata sorted by name.
            Assert.Equal(columns[table], await RowsAsync(database.Connection, $"PRAGMA table_info('{table}')"));
        Assert.Equal(indexes, await RowsAsync(database.Connection,
            "SELECT name, sql FROM sqlite_master WHERE type = 'index' AND name LIKE 'IX_%' ORDER BY name"));
        Assert.Equal(otherForeignKey, await RowsAsync(database.Connection, "PRAGMA foreign_key_list('VerificationCodes')"));
        Assert.Equal("[]", await RowsAsync(database.Connection, "PRAGMA foreign_key_check"));
        using var command = database.Connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys";
        Assert.Equal(1L, await command.ExecuteScalarAsync());
        var foreignKey = await RowsAsync(database.Connection, "PRAGMA foreign_key_list('Patients')");
        Assert.Contains("ClinicId", foreignKey);
        Assert.Contains("UserId", foreignKey);
        Assert.Contains("RESTRICT", foreignKey);

        database.Db.Patients.Add(new Patient { ClinicId = 202, UserId = 12, FullName = "Next ID", Phone = "22222222" });
        await database.Db.SaveChangesAsync();
        Assert.True(await database.Db.Patients.AnyAsync(p => p.Id > 21));
    }

    [Fact]
    public async Task Upgrade_rejects_conflicting_old_data_without_repair_or_deletion()
    {
        await using var database = new Database();
        await database.InitializeAsync(upgrade: false);
        await database.Db.Database.ExecuteSqlRawAsync("UPDATE Patients SET UserId = 12 WHERE Id = 21");
        var before = await SnapshotAsync(database.Db);
        var error = await Assert.ThrowsAsync<SqliteException>(() => database.Db.GetService<IMigrator>().MigrateAsync(TenantMigration));
        Assert.Equal(275, error.SqliteExtendedErrorCode); // Named preflight CHECK.
        Assert.Equal(before, await SnapshotAsync(database.Db));
        Assert.DoesNotContain("20261026100808_AddPatientUserTenantIntegrity",
            await database.Db.Database.GetAppliedMigrationsAsync());
    }

    private static async Task<string> SnapshotAsync(AppDbContext db) => JsonSerializer.Serialize(new
    {
        Clinics = await db.Clinics.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Name, x.Phone, x.WorkHours }).ToListAsync(),
        Users = await db.Users.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
        Patients = await db.Patients.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
        Services = await db.Services.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
        Codes = await db.VerificationCodes.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.UserId, x.Channel, x.Purpose, x.CodeHash, x.CreatedAt, x.ExpiresAt, x.FailedAttempts, x.IsUsed }).ToListAsync()
    });

    private static async Task<string> RowsAsync(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync())
        {
            var values = new Dictionary<string, object?>();
            for (var index = 0; index < reader.FieldCount; index++)
            {
                var name = reader.GetName(index);
                // SQLite rebuild reorders columns; cid is positional metadata.
                if (sql.StartsWith("PRAGMA table_info") && name == "cid") continue;
                values[name] = reader.IsDBNull(index) ? null : reader.GetValue(index);
            }
            rows.Add(JsonSerializer.Serialize(values));
        }
        rows.Sort(StringComparer.Ordinal);
        return JsonSerializer.Serialize(rows);
    }
}