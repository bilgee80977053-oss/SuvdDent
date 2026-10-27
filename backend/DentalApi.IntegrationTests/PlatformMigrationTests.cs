using System.Text.Json;
using DentalApi.Data;
using DentalApi.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace DentalApi.IntegrationTests;

public sealed class PlatformMigrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Additive_migration_and_sql_preserve_legacy_data_schema_indexes_and_foreign_keys(bool script)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        const string previous = "20261026124849_AddClinicLocationVerification";
        var migrator = db.GetService<IMigrator>(); await migrator.MigrateAsync(previous);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO Clinics (Id,Name) VALUES (101,'Synthetic A'),(202,'Synthetic B');
            INSERT INTO Users (Id,ClinicId,FullName,PasswordHash,Role,IsPhoneVerified,IsEmailVerified,IsActive,TokenVersion)
              VALUES (11,101,'Synthetic','synthetic-only','Patient',0,0,1,3);
            INSERT INTO Patients (Id,ClinicId,UserId,FullName,Phone,CreatedAt,IsActive) VALUES (41,101,11,'Synthetic','','2026-01-01',1);
            INSERT INTO Services (Id,ClinicId,Name,Description,MinPrice,IsActive) VALUES (51,101,'Synthetic','',0,1);
            INSERT INTO VerificationCodes (Id,UserId,Channel,Purpose,CodeHash,CreatedAt,ExpiresAt,FailedAttempts,IsUsed)
              VALUES (71,11,'email','registration','synthetic-only','2026-01-01','2026-01-02',0,0);
            """);
        var tables = new List<string>();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name NOT LIKE '__EF%' ORDER BY name";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
        }
        var before = new Dictionary<string,string>();
        foreach (var t in tables) before[t] = await Query(connection, $"SELECT * FROM {t} ORDER BY rowid");
        var schema = await Query(connection, "SELECT type,name,sql FROM sqlite_master WHERE name NOT LIKE 'sqlite_%' AND name NOT LIKE '__EF%' ORDER BY type,name");
        var target = db.Database.GetMigrations().Single(m => m.EndsWith("_AddPlatformAccounts"));
        if (script)
        {
            var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../DentalApi/Migrations/AddPlatformAccounts.sql"));
            var sql = await File.ReadAllTextAsync(path);
            Assert.Equal(migrator.GenerateScript(previous, target).Trim(), sql.Trim());
            await db.Database.ExecuteSqlRawAsync(sql);
        }
        else await migrator.MigrateAsync(target);
        foreach (var t in tables) Assert.Equal(before[t], await Query(connection, $"SELECT * FROM {t} ORDER BY rowid"));
        Assert.Equal(schema, await Query(connection, "SELECT type,name,sql FROM sqlite_master WHERE name NOT LIKE 'sqlite_%' AND name NOT LIKE '__EF%' AND name NOT LIKE '%PlatformAccounts%' AND name NOT LIKE '%PlatformVerificationCodes%' AND name NOT LIKE '%ClinicPatientLinks%' ORDER BY type,name"));
        Assert.Empty(await db.PlatformAccounts.ToArrayAsync()); Assert.Empty(await db.ClinicPatientLinks.ToArrayAsync());
        Assert.Empty(await db.PlatformVerificationCodes.ToArrayAsync());
        Assert.Equal("[]", await Query(connection, "PRAGMA foreign_key_check"));
        Assert.Equal("[[\"ok\"]]", await Query(connection, "PRAGMA integrity_check"));
        Assert.Equal(db.Database.GetMigrations().SkipWhile(m => m != target).Skip(1),
            await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal(target, (await db.Database.GetAppliedMigrationsAsync()).Last());
        db.PlatformAccounts.Add(new PlatformAccount { FullName = "Synthetic", Email = "a@example.invalid", PasswordHash = "synthetic-only" });
        await db.SaveChangesAsync();
        var id = await db.PlatformAccounts.Select(x => x.Id).SingleAsync();
        db.ClinicPatientLinks.Add(new ClinicPatientLink { PlatformAccountId = id, ClinicId = 101, PatientId = 41 });
        await db.SaveChangesAsync();
        var duplicate = await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO ClinicPatientLinks (PlatformAccountId,ClinicId,PatientId,CreatedAt) VALUES (1,101,41,'2026-01-01')"));
        Assert.Equal(2067, duplicate.SqliteExtendedErrorCode);
        var email = await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO PlatformAccounts (FullName,Email,PasswordHash,IsActive,IsEmailVerified,IsPhoneVerified,TokenVersion,CreatedAt) VALUES ('Synthetic','A@EXAMPLE.INVALID','synthetic-only',0,0,0,0,'2026-01-01')"));
        Assert.Equal(2067, email.SqliteExtendedErrorCode);
        var delete = await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM Patients WHERE Id=41"));
        Assert.Contains(delete.SqliteExtendedErrorCode, new[] { 787, 1811 });
        Assert.Equal("[]", await Query(connection, "PRAGMA foreign_key_check"));
    }
    private static async Task<string> Query(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand(); command.CommandText = sql;
        using var reader = await command.ExecuteReaderAsync(); var rows = new List<object?[]>();
        while (await reader.ReadAsync())
        {
            var row = new object?[reader.FieldCount];
            for (var i = 0; i < row.Length; i++) row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }
        return JsonSerializer.Serialize(rows);
    }
}
