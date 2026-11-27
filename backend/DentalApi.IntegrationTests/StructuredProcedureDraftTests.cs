using System.Data;
using System.Data.Common;
using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using DentalApi.Clinical;
using DentalApi.Data;
using DentalApi.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DentalApi.IntegrationTests;

public sealed class StructuredProcedureDraftTests
{
    [Fact]
    public async Task Uses_the_assigned_encounter_and_authoritative_label_without_prices()
    {
        using var app = await CreateFixture();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<StructuredProcedureDraftService>();
        var doctor = Principal(13, 101, Role.Doctor);

        var validated = await service.ValidateAsync(doctor, Input());
        Assert.Equal(StructuredProcedureDraftStatus.Validated, validated.Status);
        var snapshot = Assert.IsType<StructuredProcedureSnapshot>(validated.Snapshot);
        Assert.Equal((101, 41, 301, 13, 52, "Synthetic catalog label"), (snapshot.ClinicId, snapshot.PatientId,
            snapshot.AppointmentId, snapshot.DoctorUserId, snapshot.ServiceId, snapshot.ServiceLabel));
        Assert.Equal(StructuredTreatmentScope.SingleTooth, snapshot.Target.Scope);
        Assert.Equal("85", snapshot.Target.Tooth?.Code);

        var row = (await db.Services.FindAsync(52))!;
        row.Name = "Synthetic renamed service";
        row.MinPrice = 987654321;
        await db.SaveChangesAsync();
        var after = await service.ValidateAsync(doctor, new(301, 52, StructuredTreatmentScope.Visit, null));
        Assert.Equal(StructuredProcedureDraftStatus.Validated, after.Status);
        Assert.Equal("Synthetic renamed service", after.Snapshot!.ServiceLabel);
        Assert.Null(after.Snapshot.Target.Tooth);
        Assert.Equal("Synthetic catalog label", snapshot.ServiceLabel);
        Assert.Empty(await db.Set<ClinicalAuditEvent>().AsNoTracking().ToArrayAsync());
        Assert.Empty(await db.Set<TreatmentRecord>().AsNoTracking().ToArrayAsync());
    }

    [Fact]
    public async Task Does_not_release_a_draft_for_a_foreign_inactive_or_invalid_catalog_or_encounter()
    {
        using var app = await CreateFixture();
        using var scope = app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<StructuredProcedureDraftService>();
        var doctor = Principal(13, 101, Role.Doctor);
        foreach (var unavailable in new[]
        {
            Input(61), Input(53), Input(54), Input(55), Input(56), Input(int.MaxValue), Input() with { AppointmentId = 302 },
            Input() with { AppointmentId = 303 }, Input() with { AppointmentId = int.MaxValue }
        })
        {
            var result = await service.ValidateAsync(doctor, unavailable);
            Assert.Equal(StructuredProcedureDraftStatus.Unavailable, result.Status);
            Assert.Null(result.Snapshot);
        }

        foreach (var actor in new[]
        {
            Principal(23, 202, Role.Doctor), Principal(17, 101, Role.Doctor), Principal(15, 101, Role.Director),
            Principal(13, 202, Role.Doctor), new ClaimsPrincipal(new ClaimsIdentity())
        })
        {
            var result = await service.ValidateAsync(actor, Input());
            Assert.Equal(StructuredProcedureDraftStatus.Unavailable, result.Status);
            Assert.Null(result.Snapshot);
        }
    }

    [Fact]
    public async Task Rejects_invalid_targets_and_preserves_cancellation()
    {
        using var app = await CreateFixture();
        using var scope = app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<StructuredProcedureDraftService>();
        var doctor = Principal(13, 101, Role.Doctor);
        foreach (var invalid in new[]
        {
            Input(0), Input() with { AppointmentId = 0 }, Input() with { ToothCode = "86" },
            Input() with { Scope = StructuredTreatmentScope.Visit },
            Input() with { Scope = StructuredTreatmentScope.Unspecified, ToothCode = null }
        })
        {
            var result = await service.ValidateAsync(doctor, invalid);
            Assert.Equal(StructuredProcedureDraftStatus.Invalid, result.Status);
            Assert.Null(result.Snapshot);
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ValidateAsync(doctor, Input(), new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task Performed_item_persists_original_catalogue_label_with_one_audit_and_protects_replay()
    {
        using var app = await CreateFixture();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var writer = scope.ServiceProvider.GetRequiredService<PerformedProcedureService>();
        var actor = Principal(13, 101, Role.Doctor);
        var key = Guid.NewGuid();
        var result = await writer.SignAsync(actor, Input(51), key);
        Assert.Equal(PerformedProcedureStatus.Recorded, result.Status);
        Assert.Equal("Synthetic booked service", result.Receipt!.ServiceLabel);
        Assert.Equal("85", result.Receipt.Target.Tooth?.Code);

        var catalog = (await db.Services.FindAsync(51))!;
        catalog.Name = "Synthetic renamed after signing";
        await db.SaveChangesAsync();
        var replay = await writer.SignAsync(actor, Input(51), key);
        Assert.Equal(PerformedProcedureStatus.Replayed, replay.Status);
        Assert.Null(replay.Receipt);
        var conflict = await writer.SignAsync(actor, Input(51) with { ToothCode = "84" }, key);
        Assert.Equal(PerformedProcedureStatus.Conflict, conflict.Status);
        Assert.Null(conflict.Receipt);

        var row = await db.Set<PerformedProcedure>().AsNoTracking().SingleAsync();
        Assert.Equal((101, 301, 13, 51, "Synthetic booked service", "SingleTooth", "85"),
            (row.ClinicId, row.AppointmentId, row.DoctorUserId, row.ServiceId, row.ServiceLabel, row.TargetScope, row.ToothCode));
        var entry = await db.Set<ClinicalAuditEvent>().AsNoTracking().SingleAsync();
        Assert.Equal((101, 301, 13, ClinicalAuditAction.TreatmentRecorded),
            (entry.ClinicId, entry.AppointmentId, entry.ActorUserId, entry.Action));
        Assert.NotEqual(key, entry.CorrelationId);
        Assert.Empty(await db.Set<TreatmentRecord>().ToArrayAsync());
        Assert.Empty(await db.Set<TreatmentAddendum>().ToArrayAsync());
    }

    [Fact]
    public async Task Performed_sign_requires_active_booked_service_and_current_assigned_qualification()
    {
        using var app = await CreateFixture();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var writer = scope.ServiceProvider.GetRequiredService<PerformedProcedureService>();
        var doctor = Principal(13, 101, Role.Doctor);
        var key = Guid.NewGuid();

        foreach (var request in new[] { Input(52), Input(61), Input(51) with { AppointmentId = 302 },
            Input(51) with { AppointmentId = 303 } })
            Assert.Equal(PerformedProcedureStatus.Unavailable, (await writer.SignAsync(doctor, request, key)).Status);
        foreach (var actor in new[] { Principal(17, 101, Role.Doctor), Principal(23, 202, Role.Doctor),
            Principal(15, 101, Role.Director) })
            Assert.Equal(PerformedProcedureStatus.Unavailable, (await writer.SignAsync(actor, Input(51), key)).Status);
        Assert.Equal(PerformedProcedureStatus.Invalid, (await writer.SignAsync(doctor, Input(51), Guid.Empty)).Status);

        var qualification = await db.DoctorServices.SingleAsync(row => row.ServiceId == 51);
        qualification.IsActive = false;
        await db.SaveChangesAsync();
        Assert.Equal(PerformedProcedureStatus.Unavailable, (await writer.SignAsync(doctor, Input(51), key)).Status);
        qualification.IsActive = true;
        var catalog = (await db.Services.FindAsync(51))!;
        catalog.IsActive = false;
        await db.SaveChangesAsync();
        Assert.Equal(PerformedProcedureStatus.Unavailable, (await writer.SignAsync(doctor, Input(51), key)).Status);

        catalog.IsActive = true;
        await db.SaveChangesAsync();
        var host = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
        host.EnvironmentName = "Development";
        try
        {
            Assert.Equal(PerformedProcedureStatus.Unavailable, (await writer.SignAsync(doctor, Input(51), key)).Status);
        }
        finally { host.EnvironmentName = "Testing"; }

        Assert.Empty(await db.Set<PerformedProcedure>().ToArrayAsync());
        Assert.Empty(await db.Set<ClinicalAuditEvent>().ToArrayAsync());
    }

    [Fact]
    public async Task Commit_failure_rolls_back_performed_item_and_audit_then_retry_is_safe()
    {
        using var app = await CreateFixture();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var writer = scope.ServiceProvider.GetRequiredService<PerformedProcedureService>();
        var actor = Principal(13, 101, Role.Doctor);
        var key = Guid.NewGuid();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER "SyntheticProcedureAuditFailure" BEFORE INSERT ON "ClinicalAuditEvents" BEGIN
                SELECT RAISE(ABORT, 'SyntheticProcedureAuditFailure');
            END;
            """);
        var auditFailure = await writer.SignAsync(actor, Input(51), key);
        Assert.Equal(PerformedProcedureStatus.StorageUnavailable, auditFailure.Status);
        Assert.Null(auditFailure.Receipt);
        Assert.Empty(await db.Set<PerformedProcedure>().ToArrayAsync());
        await db.Database.ExecuteSqlRawAsync("""DROP TRIGGER "SyntheticProcedureAuditFailure";""");

        app.CommitFailure.Enabled = true;
        var failed = await writer.SignAsync(actor, Input(51), key);
        Assert.Equal(PerformedProcedureStatus.StorageUnavailable, failed.Status);
        Assert.Null(failed.Receipt);
        app.CommitFailure.Enabled = false;
        using var verificationScope = app.Services.CreateScope();
        var verification = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await verification.Set<PerformedProcedure>().ToArrayAsync());
        Assert.Empty(await verification.Set<ClinicalAuditEvent>().ToArrayAsync());
        Assert.False(db.ChangeTracker.HasChanges());
        var unsavedCatalog = (await db.Services.FindAsync(51))!;
        unsavedCatalog.Name = "Synthetic unsaved catalogue edit";
        Assert.Equal(PerformedProcedureStatus.StorageUnavailable, (await writer.SignAsync(actor, Input(51), key)).Status);
        Assert.Equal("Synthetic booked service", await verification.Services.Where(s => s.Id == 51).Select(s => s.Name).SingleAsync());
        db.Entry(unsavedCatalog).State = EntityState.Detached;

        // Retry using the original scope; the aborted records may not leak from its change tracker.
        Assert.Equal(PerformedProcedureStatus.Recorded, (await writer.SignAsync(actor, Input(51), key)).Status);
    }

    [Fact]
    public async Task In_memory_provider_rejects_wrong_catalogue_label_and_signed_row_mutation()
    {
        using var app = await CreateFixture();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var writer = scope.ServiceProvider.GetRequiredService<PerformedProcedureService>();
        Assert.Equal(PerformedProcedureStatus.Recorded,
            (await writer.SignAsync(Principal(13, 101, Role.Doctor), Input(51), Guid.NewGuid())).Status);
        db.Add(new PerformedProcedure
        {
            ClinicId = 101, AppointmentId = 301, DoctorUserId = 13, ServiceId = 51, RequestId = Guid.NewGuid(),
            ServiceLabel = "Synthetic untrusted label", TargetScope = "Visit", SignedUtc = DateTimeOffset.UtcNow
        });
        var invalid = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(1811, Assert.IsType<SqliteException>(invalid.InnerException).SqliteExtendedErrorCode);
        db.ChangeTracker.Clear();
        foreach (var sql in new[]
        {
            "UPDATE PerformedProcedures SET ServiceLabel = 'Synthetic changed';",
            "DELETE FROM PerformedProcedures;",
            "INSERT OR REPLACE INTO PerformedProcedures SELECT * FROM PerformedProcedures;"
        })
        {
            var failure = await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync(sql));
            Assert.Equal(1811, failure.SqliteExtendedErrorCode);
        }
        Assert.Equal("Synthetic booked service", (await db.Set<PerformedProcedure>().SingleAsync()).ServiceLabel);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Performed_procedure_migrations_are_additive_and_current(bool postgres)
    {
        using AppDbContext db = postgres ? new PostgreSqlAppDbContextDesignTimeFactory().CreateDbContext([])
            : new AppDbContextDesignTimeFactory().CreateDbContext([]);
        var ids = db.Database.GetMigrations().ToArray();
        var target = ids.Single(id => id.EndsWith(postgres ? "_AddPostgreSqlPerformedProcedures" :
            "_AddPerformedProcedures", StringComparison.Ordinal));
        var before = ids[Array.IndexOf(ids, target) - 1];
        Assert.EndsWith("TreatmentAddenda", before);
        var assembly = db.GetService<IMigrationsAssembly>();
        var migration = assembly.CreateMigration(assembly.Migrations[target], db.Database.ProviderName!);
        Assert.All(migration.UpOperations, op => Assert.True(op is
            CreateTableOperation { Name: "PerformedProcedures" } or CreateIndexOperation { Table: "PerformedProcedures" } or SqlOperation));
        var table = Assert.Single(migration.UpOperations.OfType<CreateTableOperation>());
        Assert.Equal(new[] { "ClinicId", "AppointmentId", "RequestId" }, table.PrimaryKey!.Columns);
        Assert.Equal(3, table.ForeignKeys.Count);
        Assert.All(table.ForeignKeys, fk =>
        {
            Assert.Equal("ClinicId", fk.Columns[0]);
            Assert.Equal(new[] { "ClinicId", "Id" }, fk.PrincipalColumns);
            Assert.Equal(ReferentialAction.Restrict, fk.OnDelete);
        });
        var sql = db.GetService<IMigrator>().GenerateScript(before, target, postgres
            ? MigrationsSqlGenerationOptions.Idempotent : MigrationsSqlGenerationOptions.Default);
        Assert.DoesNotContain("ALTER TABLE", sql);
        Assert.DoesNotContain("DROP TABLE", sql);
        Assert.Contains("TR_PerformedProcedures_CheckAssignment", sql);
        Assert.Contains(postgres ? "BEFORE TRUNCATE ON" : "TR_PerformedProcedures_NoReplace", sql);
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal(ConnectionState.Closed, db.Database.GetDbConnection().State);
    }

    private static StructuredProcedureDraftInput Input(int serviceId = 52)
        => new(301, serviceId, StructuredTreatmentScope.SingleTooth, "85");

    private static ClaimsPrincipal Principal(int actor, int clinic, Role role)
        => new(new ClaimsIdentity(
            [new("userId", actor.ToString()), new("clinicId", clinic.ToString()), new(ClaimTypes.Role, role.ToString())],
            "SyntheticClinic"));

    private static async Task<ProcedureFactory> CreateFixture()
    {
        var app = new ProcedureFactory();
        await app.SeedAsync(); // The source factory gates migrations on synthetic, named in-memory SQLite.
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Services.AddRange(
            new Service { Id = 51, ClinicId = 101, Name = "Synthetic booked service" },
            new Service { Id = 52, ClinicId = 101, Name = "Synthetic catalog label", MinPrice = 999, MaxPrice = 9999 },
            new Service { Id = 53, ClinicId = 101, Name = "Synthetic inactive", IsActive = false },
            new Service { Id = 54, ClinicId = 101, Name = " " },
            new Service { Id = 55, ClinicId = 101, Name = "Synthetic\nmalformed" },
            new Service { Id = 56, ClinicId = 101, Name = new string('a', 161) },
            new Service { Id = 61, ClinicId = 202, Name = "Synthetic foreign service" });
        await db.SaveChangesAsync();
        db.DoctorServices.Add(new DoctorService
        {
            ClinicId = 101, DoctorProfileId = 31, ServiceId = 51, DurationMinutes = 30
        });
        await db.SaveChangesAsync();
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();
        db.Appointments.AddRange(Encounter(301, now.AddHours(-1)),
            Encounter(302, now.AddHours(-2), AppointmentStatus.Cancelled), Encounter(303, now.AddHours(2)));
        await db.SaveChangesAsync();
        return app;
    }

    private sealed class ProcedureFactory : DoctorApiFactory
    {
        internal readonly FailingCommit CommitFailure = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services => services.AddDbContext<AppDbContext>(
                options => options.AddInterceptors(CommitFailure)));
        }
    }

    private sealed class FailingCommit : DbTransactionInterceptor
    {
        internal bool Enabled;

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (Enabled) throw new SqliteException("Synthetic performed procedure commit failure", 5);
            return ValueTask.FromResult(result);
        }
    }

    private static Appointment Encounter(int id, DateTimeOffset start, AppointmentStatus status = AppointmentStatus.Confirmed) => new()
    {
        Id = id, ClinicId = 101, PatientId = 41, DoctorProfileId = 31, ServiceId = 51,
        StartUtc = start, EndUtc = start.AddMinutes(30), OccupiedStartUtc = start, OccupiedEndUtc = start.AddMinutes(30),
        DurationMinutesSnapshot = 30, ClinicTimeZoneIdSnapshot = "Asia/Ulaanbaatar", RequestId = Guid.NewGuid(), Status = status
    };
}
