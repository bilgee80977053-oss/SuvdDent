using System.Data;
using DentalApi.Data;
using DentalApi.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace DentalApi.IntegrationTests;

public sealed class NotificationMigrationTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Both_provider_migrations_are_additive_tenant_safe_and_snapshots_current(bool postgres)
    {
        using AppDbContext db = postgres
            ? new PostgreSqlAppDbContextDesignTimeFactory().CreateDbContext([])
            : new AppDbContextDesignTimeFactory().CreateDbContext([]);
        var assembly = db.GetService<IMigrationsAssembly>();
        var entry = Assert.Single(assembly.Migrations, x => x.Key.EndsWith(postgres
            ? "_AddPostgreSqlNotificationOutbox" : "_AddNotificationOutbox"));
        var migration = assembly.CreateMigration(entry.Value, db.Database.ProviderName!);
        Assert.All(migration.UpOperations, x => Assert.True(x is
            AddUniqueConstraintOperation { Table: "Appointments" } or
            CreateTableOperation { Name: "NotificationOutbox" or "NotificationPreference" } or
            CreateIndexOperation { Table: "NotificationOutbox" }));
        var table = Assert.Single(migration.UpOperations.OfType<CreateTableOperation>(), x => x.Name == "NotificationOutbox");
        var fk = Assert.Single(table.ForeignKeys);
        Assert.Equal(new[] { "ClinicId", "AppointmentId" }, fk.Columns);
        Assert.Equal(new[] { "ClinicId", "Id" }, fk.PrincipalColumns);
        Assert.Equal(ReferentialAction.Restrict, fk.OnDelete);
        Assert.Contains(migration.UpOperations.OfType<CreateIndexOperation>(), x => x.IsUnique &&
            x.Columns.SequenceEqual(new[] { "ClinicId", "AppointmentId", "Event", "Channel" }));
        var previous = postgres ? "20261027180725_AddPostgreSqlAppointmentFoundation" : "20261027180600_AddAppointmentFoundation";
        var script = db.GetService<IMigrator>().GenerateScript(previous, entry.Key,
            postgres ? MigrationsSqlGenerationOptions.Idempotent : MigrationsSqlGenerationOptions.Default);
        Assert.Contains("CREATE TABLE \"NotificationOutbox\"", script);
        if (postgres)
        {
            Assert.DoesNotContain("DROP TABLE", script);
            Assert.DoesNotContain("ef_temp_", script);
            Assert.Equal("bigint", Assert.Single(table.Columns, x => x.Name == "DueUtcTicks").ColumnType);
        }
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal(ConnectionState.Closed, db.Database.GetDbConnection().State);
    }

    [Fact]
    public async Task SQLite_upgrade_preserves_appointments_data_and_enforces_outbox_constraints()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261027180600_AddAppointmentFoundation");
        db.Clinics.AddRange(new Clinic { Id = 101, Name = "Synthetic A" }, new Clinic { Id = 202, Name = "Synthetic B" });
        await db.SaveChangesAsync();
        db.Users.AddRange(new User { Id = 11, ClinicId = 101 }, new User { Id = 13, ClinicId = 101, Role = Role.Doctor },
            new User { Id = 12, ClinicId = 202 });
        await db.SaveChangesAsync();
        db.Patients.Add(new Patient { Id = 41, ClinicId = 101, UserId = 11 });
        db.DoctorProfiles.Add(new DoctorProfile { Id = 31, ClinicId = 101, UserId = 13 });
        db.Services.Add(new Service { Id = 51, ClinicId = 101 });
        await db.SaveChangesAsync();
        var start = new DateTimeOffset(2026,11,5,1,0,0,TimeSpan.Zero);
        db.Appointments.Add(new Appointment { Id = 301, ClinicId = 101, PatientId = 41, DoctorProfileId = 31,
            ServiceId = 51, StartUtc = start, EndUtc = start.AddMinutes(30), OccupiedStartUtc = start,
            OccupiedEndUtc = start.AddMinutes(30), DurationMinutesSnapshot = 30,
            ClinicTimeZoneIdSnapshot = "Asia/Ulaanbaatar", RequestId = Guid.NewGuid() });
        await db.SaveChangesAsync();
        var before = System.Text.Json.JsonSerializer.Serialize(await db.Appointments.AsNoTracking().SingleAsync());
        var users = System.Text.Json.JsonSerializer.Serialize(await db.Users.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync());
        db.ChangeTracker.Clear();
        await migrator.MigrateAsync();
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(await db.Appointments.AsNoTracking().SingleAsync()));
        Assert.Equal(users, System.Text.Json.JsonSerializer.Serialize(await db.Users.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync()));
        Assert.Empty(await db.Set<NotificationOutbox>().ToArrayAsync());
        Assert.Empty(await db.Set<NotificationPreference>().ToArrayAsync());
        using var check = connection.CreateCommand();
        check.CommandText = "PRAGMA integrity_check";
        Assert.Equal("ok", await check.ExecuteScalarAsync());
        check.CommandText = "PRAGMA foreign_key_check";
        Assert.Null(await check.ExecuteScalarAsync());

        var row = new NotificationOutbox { ClinicId = 101, AppointmentId = 301, Recipient = "synthetic@example.invalid",
            Body = "Synthetic", Channel = NotificationChannel.Email, Event = NotificationEvent.Confirmed };
        db.Add(row); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        db.Add(new NotificationOutbox { ClinicId = 101, AppointmentId = 301, Channel = row.Channel, Event = row.Event });
        var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(2067, Assert.IsType<SqliteException>(duplicate.InnerException).SqliteExtendedErrorCode);
        db.ChangeTracker.Clear();
        db.Add(new NotificationOutbox { ClinicId = 202, AppointmentId = 301 });
        var foreign = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(787, Assert.IsType<SqliteException>(foreign.InnerException).SqliteExtendedErrorCode);
        db.ChangeTracker.Clear();
        db.Add(new NotificationPreference { ClinicId = 202, UserId = 11, EmailEnabled = true });
        var foreignConsent = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(787, Assert.IsType<SqliteException>(foreignConsent.InnerException).SqliteExtendedErrorCode);
    }
}
