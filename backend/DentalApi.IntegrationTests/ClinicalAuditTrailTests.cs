using System.Data;
using DentalApi.Data;
using DentalApi.Models;
using DentalApi.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace DentalApi.IntegrationTests;

public sealed class ClinicalAuditTrailTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Audit_migration_is_additive_and_has_separate_current_provider_snapshot(bool postgres)
    {
        using AppDbContext db = postgres
            ? new PostgreSqlAppDbContextDesignTimeFactory().CreateDbContext([])
            : new AppDbContextDesignTimeFactory().CreateDbContext([]);
        var ids = db.Database.GetMigrations().ToArray();
        var current = ids.Single(id => id.EndsWith(postgres
            ? "_AddPostgreSqlClinicalAuditTrail" : "_AddClinicalAuditTrail", StringComparison.Ordinal));
        var preceding = ids[Array.IndexOf(ids, current) - 1];
        Assert.Equal(postgres ? "20261127122656_AddPostgreSqlAppointmentReschedules"
            : "20261127122653_AddAppointmentReschedules", preceding);
        var assembly = db.GetService<IMigrationsAssembly>();
        var migration = assembly.CreateMigration(assembly.Migrations[current], db.Database.ProviderName!);
        Assert.All(migration.UpOperations, operation => Assert.True(operation is
            CreateTableOperation { Name: "ClinicalAuditEvents" } or
            CreateIndexOperation { Table: "ClinicalAuditEvents" } or SqlOperation));
        var table = Assert.Single(migration.UpOperations.OfType<CreateTableOperation>());
        Assert.Equal(new[] { "Action", "ActorUserId", "AppointmentId", "ClinicId",
            "CorrelationId", "Id", "OccurredUtc" }, table.Columns.Select(c => c.Name).Order(StringComparer.Ordinal));
        Assert.Collection(table.ForeignKeys.OrderBy(x => x.PrincipalTable),
            fk => AssertTenantKey(fk, "Appointments", "AppointmentId"),
            fk => AssertTenantKey(fk, "Users", "ActorUserId"));
        var script = db.GetService<IMigrator>().GenerateScript(preceding, current,
            postgres ? MigrationsSqlGenerationOptions.Idempotent : MigrationsSqlGenerationOptions.Default);
        Assert.DoesNotContain("DROP TABLE", script);
        Assert.DoesNotContain("ALTER TABLE", script);
        Assert.DoesNotContain("ef_temp_", script);
        Assert.Contains(postgres ? "BEFORE TRUNCATE" : "TR_ClinicalAuditEvents_NoReplace", script);
        Assert.Contains("ClinicalAuditEvents", script);
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal(ConnectionState.Closed, db.Database.GetDbConnection().State);

        static void AssertTenantKey(AddForeignKeyOperation fk, string table, string key)
        {
            Assert.Equal(table, fk.PrincipalTable);
            Assert.Equal(new[] { "ClinicId", key }, fk.Columns);
            Assert.Equal(new[] { "ClinicId", "Id" }, fk.PrincipalColumns);
            Assert.Equal(ReferentialAction.Restrict, fk.OnDelete);
        }
    }

    [Fact]
    public async Task Sqlite_audit_rolls_back_with_a_write_and_cannot_be_updated_deleted_or_replaced()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        var migrations = db.Database.GetMigrations().ToArray();
        var auditMigration = migrations.Single(id => id.EndsWith("_AddClinicalAuditTrail", StringComparison.Ordinal));
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(migrations[Array.IndexOf(migrations, auditMigration) - 1]);
        db.Clinics.AddRange(new Clinic { Id = 101 }, new Clinic { Id = 202 });
        await db.SaveChangesAsync();
        db.Users.AddRange(new User { Id = 11, ClinicId = 101 }, new User { Id = 13, ClinicId = 101, Role = Role.Doctor },
            new User { Id = 22, ClinicId = 202, Role = Role.Doctor });
        await db.SaveChangesAsync();
        db.Patients.Add(new Patient { Id = 41, ClinicId = 101, UserId = 11 });
        db.DoctorProfiles.Add(new DoctorProfile { Id = 31, ClinicId = 101, UserId = 13 });
        db.Services.Add(new Service { Id = 51, ClinicId = 101, Description = "Synthetic before" });
        await db.SaveChangesAsync();
        db.DoctorServices.Add(new DoctorService { Id = 71, ClinicId = 101, DoctorProfileId = 31,
            ServiceId = 51, DurationMinutes = 30 });
        await db.SaveChangesAsync();
        var now = new DateTimeOffset(2026, 11, 27, 0, 0, 0, TimeSpan.Zero);
        var start = now.AddDays(-1);
        db.Appointments.Add(new Appointment { Id = 301, ClinicId = 101, PatientId = 41, DoctorProfileId = 31,
            ServiceId = 51, StartUtc = start, EndUtc = start.AddMinutes(30), OccupiedStartUtc = start,
            OccupiedEndUtc = start.AddMinutes(30), DurationMinutesSnapshot = 30,
            ClinicTimeZoneIdSnapshot = "Asia/Ulaanbaatar", RequestId = Guid.NewGuid() });
        var sourceStart = start.AddHours(-2);
        db.Appointments.Add(new Appointment { Id = 302, ClinicId = 101, PatientId = 41, DoctorProfileId = 31,
            ServiceId = 51, StartUtc = sourceStart, EndUtc = sourceStart.AddMinutes(30), OccupiedStartUtc = sourceStart,
            OccupiedEndUtc = sourceStart.AddMinutes(30), DurationMinutesSnapshot = 30,
            ClinicTimeZoneIdSnapshot = "Asia/Ulaanbaatar", RequestId = Guid.NewGuid(), Status = AppointmentStatus.Cancelled });
        await db.SaveChangesAsync();
        var moveKey = Guid.NewGuid();
        db.AppointmentReschedules.Add(new AppointmentReschedule
        {
            ClinicId = 101, SourceId = 302, ReplacementId = 301, RequestId = moveKey
        });
        await db.SaveChangesAsync();
        var overlapsBefore = await ReadOverlapTriggers();
        Assert.Equal(4, overlapsBefore.Count);
        await migrator.MigrateAsync(auditMigration);
        Assert.Equal(overlapsBefore, await ReadOverlapTriggers());
        var move = await db.AppointmentReschedules.AsNoTracking().SingleAsync();
        Assert.Equal((101, 302, 301, moveKey), (move.ClinicId, move.SourceId, move.ReplacementId, move.RequestId));
        Assert.Equal(AppointmentStatus.Cancelled, (await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == 302)).Status);

        var trail = new ClinicalAuditTrail(db, new FixedClock(now));
        var scope = new ClinicalEncounterScope(101, 41, 301, 13);
        var correlation = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => trail.Stage(scope, ClinicalAuditAction.HistoryRead, Guid.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() => trail.Stage(scope, (ClinicalAuditAction)100, correlation));

        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            (await db.Services.FindAsync(51))!.Description = "Synthetic during transaction";
            trail.Stage(scope, ClinicalAuditAction.TreatmentRecorded, correlation);
            await db.SaveChangesAsync();
            Assert.Single(await db.Set<ClinicalAuditEvent>().AsNoTracking().ToListAsync());
            await transaction.RollbackAsync();
        }
        db.ChangeTracker.Clear();
        Assert.Empty(await db.Set<ClinicalAuditEvent>().ToListAsync());
        Assert.Equal("Synthetic before", (await db.Services.FindAsync(51))!.Description);

        trail.Stage(scope, ClinicalAuditAction.HistoryRead, correlation);
        await db.SaveChangesAsync();
        var recorded = await db.Set<ClinicalAuditEvent>().AsNoTracking().SingleAsync();
        Assert.Equal((101, 301, 13, now, correlation, ClinicalAuditAction.HistoryRead),
            (recorded.ClinicId, recorded.AppointmentId, recorded.ActorUserId, recorded.OccurredUtc,
                recorded.CorrelationId, recorded.Action));

        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA recursive_triggers=OFF";
            await pragma.ExecuteNonQueryAsync();
        }
        foreach (var sql in new[]
        {
            "UPDATE ClinicalAuditEvents SET Action = 'TreatmentAmended'",
            "DELETE FROM ClinicalAuditEvents",
            "INSERT OR REPLACE INTO ClinicalAuditEvents SELECT * FROM ClinicalAuditEvents"
        })
        {
            var error = await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync(sql));
            Assert.Equal(1811, error.SqliteExtendedErrorCode);
        }

        foreach (var badId in new[] { "synthetic non-GUID text", correlation.ToString() + "\u0000suffix" })
        {
            var error = await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO ClinicalAuditEvents (ClinicId, AppointmentId, ActorUserId, Action, OccurredUtc, CorrelationId)
                VALUES (101, 301, 13, 'HistoryRead', {now.Ticks}, {badId})
                """));
            Assert.Equal(275, error.SqliteExtendedErrorCode);
        }

        var badInstant = await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO ClinicalAuditEvents (ClinicId, AppointmentId, ActorUserId, Action, OccurredUtc, CorrelationId)
            VALUES (101, 301, 13, 'HistoryRead', 1.75, {Guid.NewGuid()})
            """));
        Assert.Equal(275, badInstant.SqliteExtendedErrorCode);

        foreach (var invalidScope in new[] { scope with { DoctorUserId = 22 },
            scope with { ClinicId = 202, DoctorUserId = 22 } })
        {
            db.ChangeTracker.Clear();
            trail.Stage(invalidScope, ClinicalAuditAction.HistoryRead, Guid.NewGuid());
            var failure = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal(787, Assert.IsType<SqliteException>(failure.InnerException).SqliteExtendedErrorCode);
        }
        db.ChangeTracker.Clear();
        (await db.Services.FindAsync(51))!.Description = "Synthetic write with rejected audit";
        trail.Stage(scope with { DoctorUserId = 22 }, ClinicalAuditAction.TreatmentRecorded, Guid.NewGuid());
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        Assert.Equal("Synthetic before", (await db.Services.FindAsync(51))!.Description);
        Assert.Equal(correlation, (await db.Set<ClinicalAuditEvent>().AsNoTracking().SingleAsync()).CorrelationId);
        Assert.Equal(AppointmentStatus.Confirmed, (await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == 301)).Status);

        async Task<List<string>> ReadOverlapTriggers()
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT sql FROM sqlite_master WHERE type='trigger' AND tbl_name='Appointments' ORDER BY name";
            using var reader = await cmd.ExecuteReaderAsync();
            var definitions = new List<string>();
            while (await reader.ReadAsync())
                definitions.Add(reader.GetString(0));
            return definitions;
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
