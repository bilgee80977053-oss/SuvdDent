using System.Text.Json;
using DentalApi.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace DentalApi.IntegrationTests;

public sealed class SchedulingMigrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Migration_and_generated_sql_preserve_existing_data_and_constraints(bool useScript)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        const string previous = "20261026104238_AddDoctorProfiles";
        var target = db.Database.GetMigrations().Single(m => m.EndsWith("_AddSchedulingFoundations"));
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(previous);
        // Seed using the historical schema, without running the API or its seeder.
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO Clinics (Id,Name,Phone,WorkHours) VALUES (101,'Synthetic A',NULL,'Synthetic'),(202,'Synthetic B',NULL,NULL);
            INSERT INTO Users (Id,ClinicId,FullName,Phone,Email,PasswordHash,Role,IsPhoneVerified,IsEmailVerified,IsActive,TokenVersion,CreatedAt)
              VALUES (11,101,'Synthetic A',NULL,NULL,'synthetic-only','Doctor',0,0,1,4,'2026-01-01'),
                     (12,202,'Synthetic B',NULL,NULL,'synthetic-only','Patient',0,0,1,5,NULL);
            INSERT INTO Patients (Id,ClinicId,UserId,FullName,Phone,DateOfBirth,Address,Allergies,MedicalNotes,CreatedAt,IsActive)
              VALUES (41,101,11,'Synthetic A','00000000',NULL,'Synthetic','Synthetic','Synthetic','2026-01-01',1),
                     (42,202,NULL,'Unlinked B','00000000',NULL,NULL,NULL,NULL,'2026-01-01',0);
            INSERT INTO Services (Id,ClinicId,Name,Description,ImagePath,MinPrice,MaxPrice,AftercareText,IsActive)
              VALUES (51,101,'Synthetic','Synthetic',NULL,100,200,'Synthetic',1),(61,202,'Synthetic B','Synthetic',NULL,100,NULL,NULL,0);
            INSERT INTO VerificationCodes (Id,UserId,Channel,Purpose,CodeHash,CreatedAt,ExpiresAt,FailedAttempts,IsUsed)
              VALUES (71,11,'email','registration','synthetic-only','2026-01-01','2026-01-02',2,0);
            INSERT INTO DoctorProfiles (Id,ClinicId,UserId,Specialty,PracticeStartYear,Biography,PhotoPath,IsPublished,IsActive,CreatedAt,UpdatedAt,Version)
              VALUES (31,101,11,'Synthetic',2010,'Synthetic',NULL,1,1,'2026-01-01','2026-01-01','00000000-0000-0000-0000-000000000001');
            INSERT INTO DoctorWorkingHours (Id,ClinicId,DoctorProfileId,DayOfWeek,StartMinute,EndMinute) VALUES (81,101,31,1,540,600);
            """);
        var tables = new[] { "Users", "Patients", "Services", "VerificationCodes", "DoctorProfiles", "DoctorWorkingHours" };
        var rows = new Dictionary<string, string>();
        var columns = new Dictionary<string, string>();
        var fks = new Dictionary<string, string>();
        foreach (var table in tables)
        {
            rows[table] = await Query(connection, $"SELECT * FROM {table} ORDER BY Id", named: true);
            columns[table] = await Query(connection, $"PRAGMA table_info('{table}')", named: true);
            fks[table] = await Query(connection, $"PRAGMA foreign_key_list('{table}')", named: true);
        }
        var clinics = await Query(connection, "SELECT Id,Name,Phone,WorkHours FROM Clinics ORDER BY Id");
        var oldIndexes = await Query(connection, "SELECT name,sql FROM sqlite_master WHERE type='index' AND name LIKE 'IX_%' ORDER BY name");
        if (useScript)
        {
            // Execute the same generated SQL that is shipped for review, in memory only.
            var source = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../DentalApi/Migrations/AddSchedulingFoundations.sql"));
            Assert.Equal(migrator.GenerateScript(previous, target).Trim(), (await File.ReadAllTextAsync(source)).Trim());
            await db.Database.ExecuteSqlRawAsync(await File.ReadAllTextAsync(source));
        }
        else await migrator.MigrateAsync(target);
        foreach (var table in tables)
        {
            Assert.Equal(rows[table], await Query(connection, $"SELECT * FROM {table} ORDER BY Id", named: true));
            Assert.Equal(columns[table], await Query(connection, $"PRAGMA table_info('{table}')", named: true));
            Assert.Equal(fks[table], await Query(connection, $"PRAGMA foreign_key_list('{table}')", named: true));
        }
        Assert.Equal(clinics, await Query(connection, "SELECT Id,Name,Phone,WorkHours FROM Clinics ORDER BY Id"));
        Assert.Equal(oldIndexes, await Query(connection, "SELECT name,sql FROM sqlite_master WHERE type='index' AND name LIKE 'IX_%' AND name NOT LIKE 'IX_DoctorServices_%' AND name NOT LIKE 'IX_PatientAvailabilities_%' ORDER BY name"));
        Assert.All(await db.Clinics.Select(c => c.TimeZoneId).ToArrayAsync(), zone => Assert.Equal("Asia/Ulaanbaatar", zone));
        Assert.NotNull(TimeZoneInfo.FindSystemTimeZoneById("Asia/Ulaanbaatar"));
        Assert.Empty(await db.DoctorServices.ToArrayAsync());
        Assert.Empty(await db.PatientAvailabilitySchedules.ToArrayAsync());
        Assert.Empty(await db.PatientAvailabilities.ToArrayAsync());
        Assert.Equal("[]", await Query(connection, "PRAGMA foreign_key_check"));
        Assert.Equal("[[\"ok\"]]", await Query(connection, "PRAGMA integrity_check"));
        Assert.Equal(target, (await db.Database.GetAppliedMigrationsAsync()).Last());
        Assert.DoesNotContain(target, await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal("[[0]]", await Query(connection, "SELECT COUNT(*) FROM sqlite_master WHERE name IN ('Appointments','DoctorTimeOffs')"));
        var doctorFks = await Query(connection, "PRAGMA foreign_key_list('DoctorServices')");
        Assert.Contains("DoctorProfileId", doctorFks);
        Assert.Contains("ServiceId", doctorFks);
        Assert.Contains("RESTRICT", doctorFks);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO Services (ClinicId,Name,Description,MinPrice,IsActive) VALUES (101,'New','',0,1);
            INSERT INTO Patients (ClinicId,FullName,Phone,CreatedAt,IsActive) VALUES (101,'New','','2026-01-01',1);
            """);
        Assert.True(await db.Services.AnyAsync(x => x.Id > 61));
        Assert.True(await db.Patients.AnyAsync(x => x.Id > 42));
        // The existing nullable user link and tenant FK still work after rebuilding Patients.
        var bad = await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("UPDATE Patients SET UserId=12 WHERE Id=41"));
        Assert.Equal(787, bad.SqliteExtendedErrorCode);
    }

    private static async Task<string> Query(SqliteConnection connection, string sql, bool named = false)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync())
        {
            if (named)
            {
                var values = new SortedDictionary<string, object?>();
                for (var i = 0; i < reader.FieldCount; i++)
                    if (reader.GetName(i) != "cid") values[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                rows.Add(JsonSerializer.Serialize(values));
            }
            else
            {
                var values = new object?[reader.FieldCount];
                for (var i = 0; i < reader.FieldCount; i++) values[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                rows.Add(JsonSerializer.Serialize(values));
            }
        }
        if (named) rows.Sort(StringComparer.Ordinal);
        return "[" + string.Join(",", rows) + "]";
    }
}
