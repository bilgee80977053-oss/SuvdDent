using System.Text.Json;
using DentalApi.Data;
using DentalApi.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace DentalApi.IntegrationTests;

public sealed class ClinicLocationMigrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Sequential_migrations_preserve_data_and_leave_every_legacy_clinic_pending(bool script)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261026104238_AddDoctorProfiles");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO Clinics (Id,Name,Phone,WorkHours) VALUES (101,'Synthetic A',NULL,'Synthetic hours'),(202,'Synthetic B',NULL,NULL);
            INSERT INTO Users (Id,ClinicId,FullName,PasswordHash,Role,IsPhoneVerified,IsEmailVerified,IsActive,TokenVersion)
              VALUES (15,101,'Synthetic','synthetic-only','Director',0,0,1,0),(25,202,'Synthetic','synthetic-only','Director',0,0,1,0),
                     (13,101,'Synthetic','synthetic-only','Doctor',0,0,1,0);
            INSERT INTO Patients (Id,ClinicId,UserId,FullName,Phone,CreatedAt,IsActive) VALUES (41,101,NULL,'Synthetic','','2026-01-01',1);
            INSERT INTO Services (Id,ClinicId,Name,Description,MinPrice,IsActive) VALUES (51,101,'Synthetic','',0,1);
            INSERT INTO VerificationCodes (Id,UserId,Channel,Purpose,CodeHash,CreatedAt,ExpiresAt,FailedAttempts,IsUsed)
              VALUES (71,15,'email','registration','synthetic-only','2026-01-01','2026-01-02',0,0);
            INSERT INTO DoctorProfiles (Id,ClinicId,UserId,Specialty,PracticeStartYear,IsPublished,IsActive,CreatedAt,UpdatedAt,Version)
              VALUES (31,101,13,'Synthetic',2010,1,1,'2026-01-01','2026-01-01','00000000-0000-0000-0000-000000000001');
            INSERT INTO DoctorWorkingHours (ClinicId,DoctorProfileId,DayOfWeek,StartMinute,EndMinute) VALUES (101,31,1,540,600);
            """);
        const string scheduling = "20261026120838_AddSchedulingFoundations";
        // Both migrations run sequentially on this synthetic in-memory database only.
        await migrator.MigrateAsync(scheduling);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO DoctorServices (ClinicId,DoctorProfileId,ServiceId,DurationMinutes,BufferBeforeMinutes,BufferAfterMinutes,IsActive) VALUES (101,31,51,30,5,5,1);
            INSERT INTO PatientAvailabilitySchedules (ClinicId,PatientId,Version) VALUES (101,41,'00000000-0000-0000-0000-000000000001');
            INSERT INTO PatientAvailabilities (ClinicId,PatientId,StartUtc,EndUtc) VALUES (101,41,100,200);
            """);
        var tables = new[] { "Users", "Patients", "Services", "VerificationCodes", "DoctorProfiles", "DoctorWorkingHours", "DoctorServices", "PatientAvailabilitySchedules", "PatientAvailabilities" };
        var before = new Dictionary<string,string>();
        foreach (var table in tables)
            before[table] = await Query(connection, $"SELECT * FROM {table} ORDER BY rowid");
        var clinics = await Query(connection, "SELECT Id,Name,Phone,WorkHours,TimeZoneId FROM Clinics ORDER BY Id");
        var indexes = await Query(connection, "SELECT name,sql FROM sqlite_master WHERE type='index' AND name LIKE 'IX_%' ORDER BY name");
        var schema = await Query(connection, "SELECT name,sql FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name NOT LIKE '__EF%' AND name != 'Clinics' ORDER BY name");
        var target = db.Database.GetMigrations().Single(m => m.EndsWith("_AddClinicLocationVerification"));
        if (script)
        {
            var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../DentalApi/Migrations/AddClinicLocationVerification.sql"));
            var sql = await File.ReadAllTextAsync(path);
            Assert.Equal(migrator.GenerateScript(scheduling, target).Trim(), sql.Trim());
            await db.Database.ExecuteSqlRawAsync(sql);
        }
        else await migrator.MigrateAsync(target);
        foreach (var table in tables) Assert.Equal(before[table], await Query(connection, $"SELECT * FROM {table} ORDER BY rowid"));
        Assert.Equal(clinics, await Query(connection, "SELECT Id,Name,Phone,WorkHours,TimeZoneId FROM Clinics ORDER BY Id"));
        Assert.Equal(indexes, await Query(connection, "SELECT name,sql FROM sqlite_master WHERE type='index' AND name LIKE 'IX_%' AND name NOT LIKE 'IX_Clinics_%' ORDER BY name"));
        Assert.Equal(schema, await Query(connection, "SELECT name,sql FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name NOT LIKE '__EF%' AND name != 'Clinics' ORDER BY name"));
        Assert.All(await db.Clinics.ToArrayAsync(), c =>
        {
            Assert.Equal(TimeZoneStatus.PendingVerification, c.TimeZoneStatus);
            Assert.Null(c.CountryCode); Assert.Null(c.ProvinceOrCapitalCode); Assert.Null(c.SoumOrDistrictCode);
            Assert.Null(c.AddressLine); Assert.Null(c.TimeZoneVerifiedByUserId); Assert.Null(c.TimeZoneVerifiedAtUtc);
            Assert.Null(c.TimeZoneVerifiedClinicId);
        });
        Assert.Equal("[]", await Query(connection, "PRAGMA foreign_key_check"));
        Assert.Equal("[[\"ok\"]]", await Query(connection, "PRAGMA integrity_check"));
        Assert.DoesNotContain(target, await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(target, (await db.Database.GetAppliedMigrationsAsync()).Last());
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Contains("AUTOINCREMENT", await Query(connection, "SELECT sql FROM sqlite_master WHERE name='Clinics'"));
        Assert.Equal("[[0]]", await Query(connection, "SELECT COUNT(*) FROM sqlite_master WHERE name IN ('Appointments','DoctorTimeOffs','Notifications')"));
        // Existing clinics can immediately use their legacy Version, including Guid.Empty.
        var legacy = await db.Clinics.SingleAsync(c => c.Id == 101);
        legacy.CountryCode = "MN"; legacy.ProvinceOrCapitalCode = "11";
        legacy.SoumOrDistrictCode = "1101"; legacy.AddressLine = "Synthetic address";
        legacy.TimeZoneStatus = TimeZoneStatus.Verified; legacy.TimeZoneVerifiedByUserId = 15;
        legacy.TimeZoneVerifiedClinicId = 101; legacy.TimeZoneVerifiedAtUtc = DateTime.UtcNow;
        legacy.Version = Guid.NewGuid();
        await db.SaveChangesAsync();
        db.Clinics.Add(new Clinic { Name = "Synthetic generated ID" });
        await db.SaveChangesAsync();
        Assert.True(await db.Clinics.AnyAsync(c => c.Id > 202));
        Assert.Equal("[]", await Query(connection, "PRAGMA foreign_key_check"));
    }

    private static async Task<string> Query(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand(); command.CommandText = sql;
        using var reader = await command.ExecuteReaderAsync();
        var rows = new List<object?[]>();
        while (await reader.ReadAsync())
        {
            var row = new object?[reader.FieldCount];
            for (var i = 0; i < row.Length; i++) row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }
        return JsonSerializer.Serialize(rows);
    }
}
