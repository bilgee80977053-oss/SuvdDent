using System.Text.Json;
using DentalApi.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace DentalApi.IntegrationTests;

public sealed class AppointmentMigrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Migration_and_sql_preserve_existing_links_and_schema_and_install_overlap_triggers(bool script)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True"); await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        const string previous = "20261026131551_AddPlatformAccounts";
        var migrator = db.GetService<IMigrator>(); await migrator.MigrateAsync(previous);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO Clinics (Id,Name) VALUES (101,'Synthetic');
            INSERT INTO Users (Id,ClinicId,FullName,PasswordHash,Role,IsPhoneVerified,IsEmailVerified,IsActive,TokenVersion)
              VALUES (11,101,'Synthetic','synthetic-only','Patient',0,0,1,0);
            INSERT INTO Patients (Id,ClinicId,UserId,FullName,Phone,CreatedAt,IsActive) VALUES (41,101,11,'Synthetic','','2026-01-01',1);
            INSERT INTO PlatformAccounts (Id,FullName,Email,PasswordHash,IsActive,IsEmailVerified,IsPhoneVerified,TokenVersion,CreatedAt)
              VALUES (1,'Synthetic','synthetic@example.invalid','synthetic-only',1,1,0,0,'2026-01-01');
            INSERT INTO ClinicPatientLinks (Id,PlatformAccountId,ClinicId,PatientId,CreatedAt) VALUES (11,1,101,41,'2026-01-01');
            """);
        var tables = new List<string>();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name NOT LIKE '__EF%' ORDER BY name";
            using var reader = await cmd.ExecuteReaderAsync(); while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
        }
        var data = new Dictionary<string,string>();
        foreach (var t in tables) data[t] = await Query(connection, $"SELECT * FROM {t} ORDER BY rowid");
        var schema = await Query(connection, "SELECT type,name,sql FROM sqlite_master WHERE name NOT LIKE 'sqlite_%' AND name NOT LIKE '__EF%' AND name != 'ClinicPatientLinks' ORDER BY type,name");
        var sequences = await Query(connection, "SELECT name,seq FROM sqlite_sequence ORDER BY name");
        var target = db.Database.GetMigrations().Single(m => m.EndsWith("_AddAppointments"));
        if (script)
        {
            var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../DentalApi/Migrations/AddAppointments.sql"));
            var sql = await File.ReadAllTextAsync(path);
            Assert.Equal(migrator.GenerateScript(previous,target).Trim(),sql.Trim());
            await db.Database.ExecuteSqlRawAsync(sql);
        }
        else await migrator.MigrateAsync(target);
        foreach (var t in tables) Assert.Equal(data[t],await Query(connection,$"SELECT * FROM {t} ORDER BY rowid"));
        Assert.Equal(schema,await Query(connection,"SELECT type,name,sql FROM sqlite_master WHERE name NOT LIKE 'sqlite_%' AND name NOT LIKE '__EF%' AND name != 'ClinicPatientLinks' AND name NOT LIKE '%Appointments%' ORDER BY type,name"));
        Assert.Equal(sequences,await Query(connection,"SELECT name,seq FROM sqlite_sequence ORDER BY name"));
        Assert.Empty(await db.Appointments.ToArrayAsync());
        Assert.Equal("[[2]]",await Query(connection,"SELECT COUNT(*) FROM sqlite_master WHERE type='trigger' AND name LIKE 'TR_Appointments_NoOverlap_%'"));
        Assert.Contains("PlatformAccountId",await Query(connection,"PRAGMA foreign_key_list('Appointments')"));
        Assert.Contains("ClinicPatientLinks",await Query(connection,"PRAGMA foreign_key_list('Appointments')"));
        Assert.Equal("[]",await Query(connection,"PRAGMA foreign_key_check"));
        Assert.Equal("[[\"ok\"]]",await Query(connection,"PRAGMA integrity_check"));
        Assert.DoesNotContain(target, await db.Database.GetPendingMigrationsAsync()); Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal(target,(await db.Database.GetAppliedMigrationsAsync()).Last());
    }
    private static async Task<string> Query(SqliteConnection db,string sql)
    {
        using var cmd = db.CreateCommand();cmd.CommandText=sql;using var reader=await cmd.ExecuteReaderAsync();var rows=new List<object>();
        while(await reader.ReadAsync())
        {
            if(sql.StartsWith("SELECT * FROM",StringComparison.Ordinal))
            {
                var row=new SortedDictionary<string,object?>();
                for(var i=0;i<reader.FieldCount;i++)row[reader.GetName(i)]=reader.IsDBNull(i)?null:reader.GetValue(i);
                rows.Add(row);
            }
            else
            {
                var row=new object?[reader.FieldCount];
                for(var i=0;i<row.Length;i++)row[i]=reader.IsDBNull(i)?null:reader.GetValue(i);
                rows.Add(row);
            }
        }
        return JsonSerializer.Serialize(rows);
    }
}
