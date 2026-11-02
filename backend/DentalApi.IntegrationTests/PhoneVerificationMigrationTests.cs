using System.Data;
using System.Text.Json;
using DentalApi.Data;
using DentalApi.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace DentalApi.IntegrationTests;

public sealed class PhoneVerificationMigrationTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Provider_migrations_are_additive_tenant_safe_and_generate_SQL_offline(bool postgres)
    {
        using AppDbContext db = postgres ? new PostgreSqlAppDbContextDesignTimeFactory().CreateDbContext([]) :
            new AppDbContextDesignTimeFactory().CreateDbContext([]);
        var assembly = db.GetService<IMigrationsAssembly>();
        var entry = Assert.Single(assembly.Migrations, x => x.Key.EndsWith(postgres ?
            "_AddPostgreSqlPhoneVerificationChallenges" : "_AddPhoneVerificationChallenges"));
        var migration = assembly.CreateMigration(entry.Value, db.Database.ProviderName!);
        Assert.All(migration.UpOperations, x => Assert.True(x is
            CreateTableOperation { Name: "PhoneVerificationChallenge" } or CreateIndexOperation { Table: "PhoneVerificationChallenge" }));
        var table = Assert.Single(migration.UpOperations.OfType<CreateTableOperation>());
        Assert.Equal(2, table.ForeignKeys.Count);
        var tenant = Assert.Single(table.ForeignKeys, x => x.PrincipalTable == "Users");
        Assert.Equal(new[] { "ClinicId", "UserId" }, tenant.Columns);
        Assert.Equal(new[] { "ClinicId", "Id" }, tenant.PrincipalColumns);
        Assert.All(table.ForeignKeys, x => Assert.Equal(ReferentialAction.Restrict, x.OnDelete));
        Assert.DoesNotContain(table.Columns, x => x.Name is "Code" or "Body" or "Payload");
        Assert.Contains(table.Columns, x => x.Name == "CodeHash");
        Assert.Contains(migration.UpOperations.OfType<CreateIndexOperation>(), x => x.IsUnique &&
            x.Columns.SequenceEqual(new[] { "ClinicId", "UserId" }) &&
            x.Filter == "NOT \"IsConsumed\" AND NOT \"IsInvalidated\"");
        Assert.Equal(postgres ? "uuid" : "TEXT", Assert.Single(table.Columns, x => x.Name == "Id").ColumnType);
        Assert.Equal(postgres ? "bigint" : "INTEGER", Assert.Single(table.Columns, x => x.Name == "ExpiresUtcTicks").ColumnType);
        Assert.Equal("PhoneVerificationChallenge", Assert.IsType<DropTableOperation>(Assert.Single(migration.DownOperations)).Name);
        var previous = db.Database.GetMigrations().TakeWhile(x => x != entry.Key).Last();
        var sql = db.GetService<IMigrator>().GenerateScript(previous, entry.Key,
            postgres ? MigrationsSqlGenerationOptions.Idempotent : MigrationsSqlGenerationOptions.Default);
        Assert.Contains("CREATE TABLE \"PhoneVerificationChallenge\"", sql);
        Assert.DoesNotContain("DROP TABLE", sql);
        Assert.DoesNotContain("ALTER TABLE", sql);
        Assert.DoesNotContain("ef_temp_", sql);
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal(ConnectionState.Closed, db.Database.GetDbConnection().State);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task SQLite_upgrade_and_generated_SQL_preserve_all_existing_rows_and_schema(bool script)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        var target = db.Database.GetMigrations().Single(x => x.EndsWith("_AddPhoneVerificationChallenges"));
        var previous = db.Database.GetMigrations().TakeWhile(x => x != target).Last();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(previous);
        db.Clinics.AddRange(new Clinic { Id = 101, Name = "Synthetic A" }, new Clinic { Id = 202, Name = "Synthetic B" });
        await db.SaveChangesAsync();
        db.Users.Add(new User { Id = 11, ClinicId = 101, Phone = "00000011", Role = Role.Patient });
        await db.SaveChangesAsync();
        db.Patients.Add(new Patient { Id = 41, ClinicId = 101, UserId = 11, Phone = "11111111" });
        db.Add(new NotificationPreference { ClinicId = 101, UserId = 11, SmsEnabled = true });
        await db.SaveChangesAsync();

        var tables = new List<string>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name NOT LIKE '__EF%' ORDER BY name";
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
        }
        var rows = new Dictionary<string, string>();
        foreach (var table in tables) rows[table] = await Query(connection, $"SELECT * FROM \"{table}\" ORDER BY rowid");
        const string schema = """
            SELECT type,name,tbl_name,sql FROM sqlite_master
            WHERE name NOT LIKE 'sqlite_%' AND name NOT LIKE '__EF%' AND tbl_name <> 'PhoneVerificationChallenge'
            ORDER BY type,name
            """;
        var before = await Query(connection, schema);
        if (script) await db.Database.ExecuteSqlRawAsync(migrator.GenerateScript(previous, target));
        else await migrator.MigrateAsync(target);
        foreach (var table in tables) Assert.Equal(rows[table], await Query(connection, $"SELECT * FROM \"{table}\" ORDER BY rowid"));
        Assert.Equal(before, await Query(connection, schema));
        Assert.Equal("[]", await Query(connection, "PRAGMA foreign_key_check"));
        Assert.Equal("[[\"ok\"]]", await Query(connection, "PRAGMA integrity_check"));
        Assert.Empty(await db.Set<PhoneVerificationChallenge>().ToArrayAsync());
        Assert.False(db.Database.HasPendingModelChanges());

        var now = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero).UtcTicks;
        PhoneVerificationChallenge Row(int clinic = 101) => new()
        {
            ClinicId = clinic, UserId = 11, PhoneSnapshot = "00000011", Destination = "+97600000011",
            CodeHash = "synthetic-hash-only", CreatedUtcTicks = now,
            ExpiresUtcTicks = now + TimeSpan.FromSeconds(120).Ticks
        };
        db.Add(Row()); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        db.Add(Row());
        var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(2067, Assert.IsType<SqliteException>(duplicate.InnerException).SqliteExtendedErrorCode);
        db.ChangeTracker.Clear();
        db.Add(Row(202));
        var foreign = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(787, Assert.IsType<SqliteException>(foreign.InnerException).SqliteExtendedErrorCode);
        db.ChangeTracker.Clear();
        var invalid = Row(); invalid.IsInvalidated = true; invalid.FailedAttempts = 6; db.Add(invalid);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private static async Task<string> Query(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand(); command.CommandText = sql;
        using var reader = await command.ExecuteReaderAsync();
        var rows = new List<object?[]>();
        while (await reader.ReadAsync())
        {
            var values = new object?[reader.FieldCount];
            for (var i = 0; i < reader.FieldCount; i++) values[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(values);
        }
        return JsonSerializer.Serialize(rows);
    }
}
