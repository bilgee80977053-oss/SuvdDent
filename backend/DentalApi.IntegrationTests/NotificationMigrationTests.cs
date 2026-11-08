using System.Text.Json;
using DentalApi.Data;
using DentalApi.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace DentalApi.IntegrationTests;

public sealed class NotificationMigrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Additive_migration_and_SQL_preserve_appointments_triggers_constraints_and_old_schema(bool script)
    {
        await using var connection=new SqliteConnection("Data Source=:memory:;Foreign Keys=True"); await connection.OpenAsync();
        await using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        var migrator=db.GetService<IMigrator>();
        const string previous="20261026141041_AddAppointments";
        await migrator.MigrateAsync(previous);
        db.Clinics.Add(new Clinic { Id=101, Name="Synthetic clinic" });
        db.Users.Add(new User { Id=11, ClinicId=101, FullName="Synthetic doctor", PasswordHash="synthetic-only", Role=Role.Doctor });
        db.Patients.Add(new Patient { Id=41, ClinicId=101, FullName="Synthetic patient", Phone="" });
        db.PlatformAccounts.Add(new PlatformAccount { Id=1, FullName="Synthetic account", Email="synthetic@example.invalid", PasswordHash="synthetic-only" });
        db.Services.Add(new Service { Id=51, ClinicId=101, Name="Synthetic service" });
        db.DoctorProfiles.Add(new DoctorProfile { Id=31, ClinicId=101, UserId=11 });
        db.ClinicPatientLinks.Add(new ClinicPatientLink { Id=1, ClinicId=101, PatientId=41, PlatformAccountId=1 });
        await db.SaveChangesAsync();
        var start=new DateTimeOffset(2026,12,7,1,0,0,TimeSpan.Zero);
        db.Appointments.Add(new Appointment { ClinicId=101, PatientId=41, PlatformAccountId=1, DoctorProfileId=31, ServiceId=51,
            StartUtc=start, EndUtc=start.AddMinutes(30), OccupiedStartUtc=start, OccupiedEndUtc=start.AddMinutes(30),
            DurationMinutesSnapshot=30, TimeZoneIdSnapshot="Asia/Ulaanbaatar", IdempotencyKey=Guid.NewGuid(), CreatedAtUtc=DateTime.UtcNow });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var rows=await Query(connection,"SELECT * FROM Appointments ORDER BY Id");
        var oldSchema=await Query(connection,"SELECT type,name,sql FROM sqlite_master WHERE name NOT LIKE 'sqlite_%' AND name NOT LIKE '__EF%' ORDER BY type,name");
        var sequences=await Query(connection,"SELECT name,seq FROM sqlite_sequence ORDER BY name");
        var target=db.Database.GetMigrations().Single(x=>x.EndsWith("_AddAppointmentNotificationOutbox"));
        var sql=migrator.GenerateScript(previous,target);
        Assert.DoesNotContain("DROP TABLE",sql,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ef_temp",sql,StringComparison.OrdinalIgnoreCase);
        if(script)
        {
            var path=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../DentalApi/Migrations/AddAppointmentNotificationOutbox.sql"));
            Assert.Equal(sql.Trim(),(await File.ReadAllTextAsync(path)).Trim());
            await db.Database.ExecuteSqlRawAsync(sql);
        }
        else await migrator.MigrateAsync(target);
        Assert.Equal(rows,await Query(connection,"SELECT * FROM Appointments ORDER BY Id"));
        Assert.Equal(sequences,await Query(connection,"SELECT name,seq FROM sqlite_sequence ORDER BY name"));
        Assert.Equal(oldSchema,await Query(connection,"SELECT type,name,sql FROM sqlite_master WHERE name NOT LIKE 'sqlite_%' AND name NOT LIKE '__EF%' AND name NOT LIKE '%AppointmentNotificationOutbox%' AND name <> 'AK_Appointments_ClinicId_PlatformAccountId_Id' ORDER BY type,name"));
        Assert.Equal("[[2]]",await Query(connection,"SELECT COUNT(*) FROM sqlite_master WHERE type='trigger' AND name LIKE 'TR_Appointments_NoOverlap_%'"));
        Assert.Empty(await db.AppointmentNotificationOutbox.ToArrayAsync());
        var appointment=await db.Appointments.SingleAsync();
        db.AppointmentNotificationOutbox.Add(new AppointmentNotification { AppointmentId=appointment.Id, ClinicId=101, PlatformAccountId=1, Channel="Email", CreatedAtUtc=DateTime.UtcNow });
        await db.SaveChangesAsync();
        var invalidTenant=await Assert.ThrowsAsync<SqliteException>(()=>db.Database.ExecuteSqlRawAsync("UPDATE AppointmentNotificationOutbox SET ClinicId=202"));
        Assert.Equal(787,invalidTenant.SqliteExtendedErrorCode);
        var invalidState=await Assert.ThrowsAsync<SqliteException>(()=>db.Database.ExecuteSqlRawAsync("UPDATE AppointmentNotificationOutbox SET Status='Delivered'"));
        Assert.Equal(275,invalidState.SqliteExtendedErrorCode);
        var invalidSnapshot=await Assert.ThrowsAsync<SqliteException>(()=>db.Database.ExecuteSqlRawAsync("UPDATE Appointments SET DurationMinutesSnapshot=31"));
        Assert.Equal(275,invalidSnapshot.SqliteExtendedErrorCode);
        var columns=new List<string>();
        using(var cmd=connection.CreateCommand())
        {
            cmd.CommandText="PRAGMA table_info('Appointments')"; using var reader=await cmd.ExecuteReaderAsync();
            while(await reader.ReadAsync()) if(reader.GetString(1)!="Id") columns.Add("\""+reader.GetString(1)+"\"");
        }
        var names=string.Join(",",columns);
        using var overlapCommand=connection.CreateCommand();
        // Identifiers come only from this test's known migration schema, never a request.
        overlapCommand.CommandText=$"INSERT INTO Appointments ({names}) SELECT {names} FROM Appointments";
        var overlap=await Assert.ThrowsAsync<SqliteException>(()=>overlapCommand.ExecuteNonQueryAsync());
        Assert.Equal(1811,overlap.SqliteExtendedErrorCode);
        Assert.Equal("[]",await Query(connection,"PRAGMA foreign_key_check"));
        Assert.Equal("[[1]]",await Query(connection,"PRAGMA foreign_keys"));
        Assert.Equal("[[\"ok\"]]",await Query(connection,"PRAGMA integrity_check"));
        Assert.Empty(await db.Database.GetPendingMigrationsAsync()); Assert.False(db.Database.HasPendingModelChanges());
    }
    private static async Task<string> Query(SqliteConnection db,string sql)
    {
        using var cmd=db.CreateCommand(); cmd.CommandText=sql; using var reader=await cmd.ExecuteReaderAsync();
        var rows=new List<object?>();
        while(await reader.ReadAsync()) { var row=new object?[reader.FieldCount]; for(var i=0;i<row.Length;i++) row[i]=reader.IsDBNull(i)?null:reader.GetValue(i); rows.Add(row); }
        return JsonSerializer.Serialize(rows);
    }
}
