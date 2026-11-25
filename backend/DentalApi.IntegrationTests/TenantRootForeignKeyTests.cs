using System.Data;
using DentalApi.Data;
using DentalApi.Models;
using DentalApi.PgCrossTenantFixture;
using DentalApi.PgDevelopmentFixture;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;
using CrossTenantFixture = DentalApi.PgCrossTenantFixture.PgCrossTenantFixture;
using DevelopmentFixture = DentalApi.PgDevelopmentFixture.PgDevelopmentFixture;

namespace DentalApi.IntegrationTests;

public sealed class TenantRootForeignKeyTests
{
    private const string PreviousSqliteMigration = "20261102180259_AddPhoneVerificationChallenges";
    private const string SqliteMigration = "20261125115238_AddClinicTenantRootForeignKeys";
    private const string PostgreSqlMigration = "20261125115303_AddPostgreSqlClinicTenantRootForeignKeys";

    private sealed class Database : IAsyncDisposable
    {
        public SqliteConnection Connection { get; } = new("Data Source=:memory:;Foreign Keys=True");
        public AppDbContext Db { get; }

        public Database() =>
            Db = new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(Connection).Options);

        public async Task InitializeAsync(string? target = null)
        {
            await Connection.OpenAsync();
            await Db.GetService<IMigrator>().MigrateAsync(target ?? SqliteMigration);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }

    [Theory]
    [InlineData("user")]
    [InlineData("service")]
    [InlineData("patient")]
    public async Task Root_rows_with_nonexistent_clinics_are_rejected(string entity)
    {
        await using var database = new Database();
        await database.InitializeAsync();
        switch (entity)
        {
            case "user":
                database.Db.Users.Add(new User { ClinicId = 999, FullName = "Synthetic", PasswordHash = "synthetic-only" });
                break;
            case "service":
                database.Db.Services.Add(new Service { ClinicId = 999, Name = "Synthetic" });
                break;
            case "patient":
                database.Db.Patients.Add(new Patient { ClinicId = 999, UserId = null, FullName = "Synthetic" });
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(entity));
        }

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => database.Db.SaveChangesAsync());
        Assert.Equal(787, Assert.IsType<SqliteException>(error.InnerException).SqliteExtendedErrorCode);
    }

    [Fact]
    public async Task Unlinked_patient_is_allowed_when_its_clinic_exists()
    {
        await using var database = new Database();
        await database.InitializeAsync();
        var clinic = new Clinic { Name = "Synthetic clinic" };
        database.Db.Clinics.Add(clinic);
        await database.Db.SaveChangesAsync();

        var patient = new Patient { ClinicId = clinic.Id, UserId = null, FullName = "Synthetic patient" };
        database.Db.Patients.Add(patient);
        await database.Db.SaveChangesAsync();

        var persisted = await database.Db.Patients.SingleAsync();
        Assert.Equal(clinic.Id, persisted.ClinicId);
        Assert.Null(persisted.UserId);
    }

    [Fact]
    public async Task Cross_clinic_patient_user_link_remains_prohibited()
    {
        await using var database = new Database();
        await database.InitializeAsync();
        database.Db.Clinics.AddRange(
            new Clinic { Id = 101, Name = "Synthetic clinic A" },
            new Clinic { Id = 202, Name = "Synthetic clinic B" });
        await database.Db.SaveChangesAsync();
        var user = new User { ClinicId = 202, FullName = "Synthetic user", PasswordHash = "synthetic-only" };
        database.Db.Users.Add(user);
        await database.Db.SaveChangesAsync();

        database.Db.Patients.Add(new Patient
        {
            ClinicId = 101,
            UserId = user.Id,
            FullName = "Synthetic patient"
        });
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => database.Db.SaveChangesAsync());
        Assert.Equal(787, Assert.IsType<SqliteException>(error.InnerException).SqliteExtendedErrorCode);
    }

    [Theory]
    [InlineData("user")]
    [InlineData("service")]
    [InlineData("patient")]
    public async Task Clinic_referenced_by_each_root_type_cannot_be_deleted(string entity)
    {
        await using var database = new Database();
        await database.InitializeAsync();
        var clinic = new Clinic { Id = 101, Name = "Synthetic clinic" };
        database.Db.Clinics.Add(clinic);
        await database.Db.SaveChangesAsync();
        switch (entity)
        {
            case "user":
                database.Db.Users.Add(new User { ClinicId = clinic.Id, FullName = "Synthetic", PasswordHash = "synthetic-only" });
                break;
            case "service":
                database.Db.Services.Add(new Service { ClinicId = clinic.Id, Name = "Synthetic" });
                break;
            case "patient":
                database.Db.Patients.Add(new Patient { ClinicId = clinic.Id, UserId = null, FullName = "Synthetic" });
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(entity));
        }
        await database.Db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<SqliteException>(() =>
            database.Db.Database.ExecuteSqlRawAsync("DELETE FROM Clinics WHERE Id = 101"));
        Assert.Contains(error.SqliteExtendedErrorCode, new[] { 787, 1811 });
        Assert.True(await database.Db.Clinics.AnyAsync(row => row.Id == clinic.Id));
    }

    [Fact]
    public async Task Pending_clinic_can_be_created_then_director_added_then_timezone_verified()
    {
        await using var database = new Database();
        await database.InitializeAsync();
        var clinic = new Clinic { Name = "Synthetic pending clinic" };
        database.Db.Clinics.Add(clinic);
        await database.Db.SaveChangesAsync();
        Assert.Equal(TimeZoneStatus.PendingVerification, clinic.TimeZoneStatus);
        Assert.Null(clinic.TimeZoneVerifiedByUserId);

        var director = new User
        {
            ClinicId = clinic.Id,
            FullName = "Synthetic director",
            PasswordHash = "synthetic-only",
            Role = Role.Director
        };
        database.Db.Users.Add(director);
        await database.Db.SaveChangesAsync();

        clinic.CountryCode = "MN";
        clinic.ProvinceOrCapitalCode = "11";
        clinic.SoumOrDistrictCode = "1101";
        clinic.AddressLine = "Synthetic address";
        clinic.TimeZoneStatus = TimeZoneStatus.Verified;
        clinic.TimeZoneId = "Asia/Ulaanbaatar";
        clinic.TimeZoneVerifiedClinicId = clinic.Id;
        clinic.TimeZoneVerifiedByUserId = director.Id;
        clinic.TimeZoneVerifiedAtUtc = DateTime.UtcNow;
        await database.Db.SaveChangesAsync();

        Assert.Equal(TimeZoneStatus.Verified, (await database.Db.Clinics.SingleAsync()).TimeZoneStatus);
    }

    [Fact]
    public void Provider_migrations_add_only_restrictive_clinic_foreign_keys_and_keep_snapshots_current()
    {
        using var sqlite = new AppDbContextDesignTimeFactory().CreateDbContext([]);
        using var pg = new PostgreSqlAppDbContextDesignTimeFactory().CreateDbContext([]);
        Assert.False(sqlite.Database.HasPendingModelChanges());
        Assert.False(pg.Database.HasPendingModelChanges());

        var sqliteMigration = ReadMigration(sqlite, SqliteMigration);
        var pgMigration = ReadMigration(pg, PostgreSqlMigration);
        AssertTenantRootOperations(sqliteMigration);
        AssertTenantRootOperations(pgMigration);
        Assert.Equal(5, pg.Database.GetMigrations().Count());
        Assert.Equal(5, DevelopmentFixture.ExpectedMigrationCount);
        Assert.Equal(5, CrossTenantFixture.ExpectedMigrationCount);

        var pgScript = pg.GetService<IMigrator>().GenerateScript(
            "20261102180315_AddPostgreSqlPhoneVerificationChallenges",
            PostgreSqlMigration,
            MigrationsSqlGenerationOptions.Idempotent);
        Assert.Contains("ALTER TABLE \"Patients\" ADD CONSTRAINT \"FK_Patients_Clinics_ClinicId\"", pgScript);
        Assert.Contains("ALTER TABLE \"Services\" ADD CONSTRAINT \"FK_Services_Clinics_ClinicId\"", pgScript);
        Assert.Contains("ALTER TABLE \"Users\" ADD CONSTRAINT \"FK_Users_Clinics_ClinicId\"", pgScript);
        Assert.DoesNotContain("CREATE TABLE", pgScript);
        Assert.DoesNotContain("CREATE INDEX", pgScript);
        Assert.DoesNotContain("DROP TABLE", pgScript);
        Assert.Equal(ConnectionState.Closed, sqlite.Database.GetDbConnection().State);
        Assert.Equal(ConnectionState.Closed, pg.Database.GetDbConnection().State);
    }

    [Fact]
    public async Task SQLite_migration_preserves_existing_rows_and_adds_all_three_root_constraints()
    {
        await using var database = new Database();
        await database.InitializeAsync(PreviousSqliteMigration);
        var clinic = new Clinic { Id = 101, Name = "Synthetic clinic" };
        database.Db.Clinics.Add(clinic);
        await database.Db.SaveChangesAsync();
        var user = new User { ClinicId = clinic.Id, FullName = "Synthetic user", PasswordHash = "synthetic-only" };
        database.Db.Users.Add(user);
        database.Db.Patients.Add(new Patient { ClinicId = clinic.Id, UserId = null, FullName = "Unlinked synthetic patient" });
        database.Db.Services.Add(new Service { ClinicId = clinic.Id, Name = "Synthetic service" });
        await database.Db.SaveChangesAsync();
        var before = await SnapshotAsync(database.Db);

        await database.Db.GetService<IMigrator>().MigrateAsync(SqliteMigration);

        Assert.Equal(before, await SnapshotAsync(database.Db));
        Assert.Null((await database.Db.Patients.SingleAsync()).UserId);
        Assert.Equal("[\"Clinics\",\"Clinics\",\"Clinics\"]", await QueryAsync(database.Connection,
            "SELECT \"table\" FROM pragma_foreign_key_list('Users') WHERE \"table\" = 'Clinics' UNION ALL " +
            "SELECT \"table\" FROM pragma_foreign_key_list('Services') WHERE \"table\" = 'Clinics' UNION ALL " +
            "SELECT \"table\" FROM pragma_foreign_key_list('Patients') WHERE \"table\" = 'Clinics'"));
        Assert.Equal("[]", await QueryAsync(database.Connection, "PRAGMA foreign_key_check"));
    }

    [Fact]
    public async Task SQLite_migration_rejects_preexisting_orphans_without_repairing_them()
    {
        await using var database = new Database();
        await database.InitializeAsync(PreviousSqliteMigration);
        database.Db.Clinics.Add(new Clinic { Id = 101, Name = "Synthetic clinic" });
        await database.Db.SaveChangesAsync();
        database.Db.Users.Add(new User { ClinicId = 901, FullName = "Orphan user", PasswordHash = "synthetic-only" });
        database.Db.Services.Add(new Service { ClinicId = 902, Name = "Orphan service" });
        database.Db.Patients.Add(new Patient { ClinicId = 903, UserId = null, FullName = "Orphan patient" });
        await database.Db.SaveChangesAsync();
        var before = await SnapshotAsync(database.Db);

        var error = await Record.ExceptionAsync(() =>
            database.Db.GetService<IMigrator>().MigrateAsync(SqliteMigration));

        Assert.Contains(Assert.IsType<SqliteException>(error).SqliteExtendedErrorCode, new[] { 787, 1811 });
        Assert.Equal(before, await SnapshotAsync(database.Db));
        Assert.DoesNotContain(SqliteMigration, await database.Db.Database.GetAppliedMigrationsAsync());
    }

    private static Migration ReadMigration(AppDbContext db, string id)
    {
        var assembly = db.GetService<IMigrationsAssembly>();
        return assembly.CreateMigration(assembly.Migrations[id], db.Database.ProviderName!);
    }

    private static Migration ReadMigration(PostgreSqlAppDbContext db, string id)
    {
        var assembly = db.GetService<IMigrationsAssembly>();
        return assembly.CreateMigration(assembly.Migrations[id], db.Database.ProviderName!);
    }

    private static void AssertTenantRootOperations(Migration migration)
    {
        var foreignKeys = migration.UpOperations.OfType<AddForeignKeyOperation>().ToArray();
        Assert.Equal(new[] { "Patients", "Services", "Users" }, foreignKeys.Select(fk => fk.Table).Order());
        Assert.All(foreignKeys, fk =>
        {
            Assert.Equal(new[] { "ClinicId" }, fk.Columns);
            Assert.Equal("Clinics", fk.PrincipalTable);
            Assert.Equal(new[] { "Id" }, fk.PrincipalColumns);
            Assert.Equal(ReferentialAction.Restrict, fk.OnDelete);
        });
        Assert.Equal(3, migration.DownOperations.OfType<DropForeignKeyOperation>().Count());
        Assert.Equal(3, migration.UpOperations.Count);
    }

    private static async Task<string> QueryAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync())
            values.Add(reader.GetString(0));
        values.Sort(StringComparer.Ordinal);
        return System.Text.Json.JsonSerializer.Serialize(values);
    }

    private static async Task<string> SnapshotAsync(AppDbContext db) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            Clinics = await db.Clinics.AsNoTracking().OrderBy(row => row.Id)
                .Select(row => new { row.Id, row.Name, row.TimeZoneStatus, row.TimeZoneVerifiedByUserId, row.TimeZoneVerifiedClinicId })
                .ToListAsync(),
            Users = await db.Users.AsNoTracking().OrderBy(row => row.Id)
                .Select(row => new { row.Id, row.ClinicId, row.FullName, row.PasswordHash, row.Role })
                .ToListAsync(),
            Patients = await db.Patients.AsNoTracking().OrderBy(row => row.Id)
                .Select(row => new { row.Id, row.ClinicId, row.UserId, row.FullName })
                .ToListAsync(),
            Services = await db.Services.AsNoTracking().OrderBy(row => row.Id)
                .Select(row => new { row.Id, row.ClinicId, row.Name, row.MinPrice, row.MaxPrice })
                .ToListAsync()
        });
}
