using System.Data;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DentalApi.Controllers;
using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
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
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DentalApi.IntegrationTests;

public sealed partial class StructuredProcedureDraftTests
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

    [Fact]
    public async Task Performed_HTTP_sign_uses_the_validated_catalogue_and_replay_does_not_return_a_record()
    {
        using var app = await CreateFixture(httpEnabled: true);
        using var doctor = await app.AsAsync(13);
        var key = Guid.NewGuid();
        var url = PerformedRoute(301);
        using var created = await doctor.PostAsJsonAsync(url, SignRequest(key));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.True(created.Headers.CacheControl?.NoStore);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(7, body.EnumerateObject().Count());
        Assert.Equal(301, body.GetProperty("appointmentId").GetInt32());
        Assert.Equal(key, body.GetProperty("requestId").GetGuid());
        Assert.Equal(51, body.GetProperty("serviceId").GetInt32());
        Assert.Equal("Synthetic booked service", body.GetProperty("serviceLabel").GetString());
        Assert.Equal("SingleTooth", body.GetProperty("scope").GetString());
        Assert.Equal("85", body.GetProperty("toothCode").GetString());
        Assert.Equal(TimeSpan.Zero, body.GetProperty("signedUtc").GetDateTimeOffset().Offset);

        using var repeated = await doctor.PostAsJsonAsync(url, SignRequest(key));
        Assert.Equal(HttpStatusCode.NoContent, repeated.StatusCode);
        Assert.True(repeated.Headers.CacheControl?.NoStore);
        Assert.Empty(await repeated.Content.ReadAsStringAsync());
        using var conflict = await doctor.PostAsJsonAsync(url, SignRequest(key) with { ToothCode = "84" });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.DoesNotContain("Synthetic booked service", await conflict.Content.ReadAsStringAsync());

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(key, (await db.Set<PerformedProcedure>().SingleAsync()).RequestId);
        Assert.Equal(ClinicalAuditAction.TreatmentRecorded, (await db.Set<ClinicalAuditEvent>().SingleAsync()).Action);
    }

    [Fact]
    public async Task Performed_HTTP_refuses_untrusted_payload_and_invalid_targets()
    {
        using var app = await CreateFixture(httpEnabled: true);
        using var doctor = await app.AsAsync(13);
        var url = PerformedRoute(301);
        var key = Guid.NewGuid();
        foreach (var invalid in new[]
        {
            SignRequest(Guid.Empty),
            SignRequest(key) with { ServiceId = 0 },
            SignRequest(key) with { ToothCode = "86" },
            SignRequest(key) with { Scope = "Visit" },
            SignRequest(key) with { Scope = "singleTooth" },
            SignRequest(key) with { Scope = null }
        })
        {
            using var rejected = await doctor.PostAsJsonAsync(url, invalid);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }
        using var untrusted = await doctor.PostAsJsonAsync(url, new
        {
            requestId = key, serviceId = 51, scope = "SingleTooth", toothCode = "85",
            clinicId = 202, patientId = 42, doctorUserId = 17, appointmentId = 302,
            serviceLabel = "Synthetic supplied label", amount = 1, price = 1
        });
        Assert.Equal(HttpStatusCode.BadRequest, untrusted.StatusCode);
        using var wrongShape = await doctor.PostAsJsonAsync(url, new { requestId = key, serviceId = "51", scope = "Visit" });
        Assert.Equal(HttpStatusCode.BadRequest, wrongShape.StatusCode);
        using var oversized = new StringContent(JsonSerializer.Serialize(SignRequest(key)) + new string(' ', 1024),
            Encoding.UTF8, "application/json");
        using var tooLarge = await doctor.PostAsync(url, oversized);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooLarge.StatusCode);
        await AssertNoProcedureWrites(app);
    }

    [Fact]
    public async Task Performed_HTTP_requires_clinic_doctor_assignee_and_active_booked_qualification()
    {
        using var app = await CreateFixture(httpEnabled: true);
        var key = Guid.NewGuid();
        using var anonymous = app.Client();
        using var patient = await app.AsAsync(11);
        using var director = await app.AsAsync(15);
        using var foreign = await app.AsAsync(23, clinicId: 202);
        using var unassigned = await app.AsAsync(17);
        using var assigned = await app.AsAsync(13);
        await Expect(anonymous, HttpStatusCode.Unauthorized);
        await Expect(patient, HttpStatusCode.Forbidden);
        await Expect(director, HttpStatusCode.Forbidden);
        await Expect(foreign, HttpStatusCode.NotFound);
        await Expect(unassigned, HttpStatusCode.NotFound);
        await Expect(assigned, HttpStatusCode.NotFound, request: SignRequest(key) with { ServiceId = 52 });
        await Expect(assigned, HttpStatusCode.NotFound, encounter: 302);
        await Expect(assigned, HttpStatusCode.NotFound, encounter: 303);

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var qualification = await db.DoctorServices.SingleAsync(row => row.ServiceId == 51);
            qualification.IsActive = false;
            await db.SaveChangesAsync();
        }
        await Expect(assigned, HttpStatusCode.NotFound);
        await AssertNoProcedureWrites(app);

        async Task Expect(HttpClient caller, HttpStatusCode status, int encounter = 301,
            SignPerformedProcedureRequest? request = null)
        {
            using var response = await caller.PostAsJsonAsync(PerformedRoute(encounter), request ?? SignRequest(key));
            Assert.Equal(status, response.StatusCode);
        }
    }

    [Fact]
    public async Task Performed_HTTP_is_off_without_flag_or_outside_testing_or_on_nonmemory_connection_metadata()
    {
        using (var offline = await CreateFixture())
        {
            using var doctor = await offline.AsAsync(13);
            using var denied = await doctor.PostAsJsonAsync(PerformedRoute(301), SignRequest(Guid.NewGuid()));
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
            using var read = await doctor.GetAsync(PerformedRoute(301));
            Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
            await AssertNoProcedureWrites(offline);
        }

        using (var memoryHost = await CreateFixture(httpEnabled: true))
        {
            using var doctor = await memoryHost.AsAsync(13);
            using var memoryScope = memoryHost.Services.CreateScope();
            var memoryEnvironment = memoryScope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
            try
            {
                foreach (var environment in new[] { "Development", "Production" })
                {
                    memoryEnvironment.EnvironmentName = environment;
                    using var denied = await doctor.PostAsJsonAsync(PerformedRoute(301), SignRequest(Guid.NewGuid()));
                    Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
                    Assert.True(denied.Headers.CacheControl?.NoStore);
                    using var read = await doctor.GetAsync(PerformedRoute(301));
                    Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
                }
            }
            finally { memoryEnvironment.EnvironmentName = "Testing"; }
            await AssertNoProcedureWrites(memoryHost);
        }

        using var app = await CreateFixture(httpEnabled: true, fakePersistentMetadata: true);
        using var clinician = await app.AsAsync(13);
        using var scope = app.Services.CreateScope();
        var host = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
        using var nonMemory = await clinician.PostAsJsonAsync(PerformedRoute(301), SignRequest(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.NotFound, nonMemory.StatusCode);
        Assert.True(nonMemory.Headers.CacheControl?.NoStore);
        using var nonMemoryRead = await clinician.GetAsync(PerformedRoute(301));
        Assert.Equal(HttpStatusCode.NotFound, nonMemoryRead.StatusCode);
        app.Services.GetRequiredService<IConfiguration>()["Features:PatientHistoryAcceptance"] = "true";
        using var nonMemoryHistory = await clinician.GetAsync(HistoryRoute(301));
        Assert.Equal(HttpStatusCode.NotFound, nonMemoryHistory.StatusCode);

        using var postgresModelOnly = new PostgreSqlAppDbContextDesignTimeFactory().CreateDbContext([]);
        Assert.False(PerformedProcedureService.IsTestingMemorySqlite(host, postgresModelOnly));
        Assert.Equal(ConnectionState.Closed, postgresModelOnly.Database.GetDbConnection().State);
        Assert.Equal(0, app.OpenGuard.PersistentAttempts);
        await AssertNoProcedureWrites(app);
    }

    [Fact]
    public async Task Performed_HTTP_gives_generic_failure_and_no_signed_result_until_transaction_commits()
    {
        using var app = await CreateFixture(httpEnabled: true);
        using var clinician = await app.AsAsync(13);
        var url = PerformedRoute(301);
        var request = SignRequest(Guid.NewGuid());
        app.CommitFailure.Enabled = true;
        using var failed = await clinician.PostAsJsonAsync(url, request);
        app.CommitFailure.Enabled = false;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.True(failed.Headers.CacheControl?.NoStore);
        var body = await failed.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Synthetic performed procedure commit failure", body);
        Assert.DoesNotContain("Synthetic booked service", body);
        Assert.DoesNotContain(request.RequestId.ToString(), body);
        await AssertNoProcedureWrites(app);

        using var retry = await clinician.PostAsJsonAsync(url, request);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
    }

    [Fact]
    public async Task Performed_HTTP_pages_only_current_assigned_doctors_signed_items_and_audits_each_page()
    {
        using var app = await CreateFixture(httpEnabled: true);
        using var doctor = await app.AsAsync(13);
        using var foreign = await app.AsAsync(23, clinicId: 202);
        using var otherDoctor = await app.AsAsync(17);
        using var patient = await app.AsAsync(11);
        using var anonymous = app.Client();
        var url = PerformedRoute(301);
        var low = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var high = Guid.Parse("99999999-9999-9999-9999-999999999999");
        foreach (var id in new[] { high, low })
        {
            using var signed = await doctor.PostAsJsonAsync(url, SignRequest(id));
            Assert.Equal(HttpStatusCode.Created, signed.StatusCode);
        }

        await Denied(anonymous, HttpStatusCode.Unauthorized);
        await Denied(patient, HttpStatusCode.Forbidden);
        await Denied(foreign, HttpStatusCode.NotFound);
        await Denied(otherDoctor, HttpStatusCode.NotFound);
        await Denied(doctor, HttpStatusCode.NotFound, PerformedRoute(302));
        foreach (var badQuery in new[] { "?page=0", "?page=1001", "?pageSize=0", "?pageSize=51" })
            await Denied(doctor, HttpStatusCode.BadRequest, url + badQuery);

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(0, await db.Set<ClinicalAuditEvent>().CountAsync(row => row.Action == ClinicalAuditAction.HistoryRead));
            var service = (await db.Services.FindAsync(51))!;
            service.Name = "Synthetic later catalogue label";
            await db.SaveChangesAsync();
        }

        await CheckPage(doctor, 1, [low], hasMore: true);
        await CheckPage(doctor, 2, [high], hasMore: false);
        await CheckPage(doctor, 1000, [], hasMore: false);

        using (var reassignment = app.Services.CreateScope())
        {
            var db = reassignment.ServiceProvider.GetRequiredService<AppDbContext>();
            var profile = (await db.DoctorProfiles.FindAsync(31))!;
            profile.UserId = 20;
            await db.SaveChangesAsync();
        }
        await Denied(doctor, HttpStatusCode.NotFound);
        using var newAssignee = await app.AsAsync(20);
        await CheckPage(newAssignee, 1, [], hasMore: false);

        using var check = app.Services.CreateScope();
        var evidence = check.ServiceProvider.GetRequiredService<AppDbContext>();
        var access = await evidence.Set<ClinicalAuditEvent>().Where(row => row.Action == ClinicalAuditAction.HistoryRead)
            .OrderBy(row => row.Id).ToArrayAsync();
        Assert.Equal(new[] { 13, 13, 13, 20 }, access.Select(row => row.ActorUserId));
        Assert.All(access, row => Assert.Equal((101, 301), (row.ClinicId, row.AppointmentId)));
        Assert.Equal(0, await evidence.Set<ClinicalAuditEvent>().CountAsync(row => row.ClinicId == 202));

        async Task Denied(HttpClient client, HttpStatusCode status, string? path = null)
        {
            using var response = await client.GetAsync(path ?? url);
            Assert.Equal(status, response.StatusCode);
            Assert.DoesNotContain("Synthetic booked service", await response.Content.ReadAsStringAsync());
        }

        async Task CheckPage(HttpClient client, int page, Guid[] expected, bool hasMore)
        {
            using var response = await client.GetAsync(url + $"?page={page}&pageSize=1");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(4, body.EnumerateObject().Count());
            Assert.Equal(page, body.GetProperty("page").GetInt32());
            Assert.Equal(1, body.GetProperty("pageSize").GetInt32());
            Assert.Equal(hasMore, body.GetProperty("hasMore").GetBoolean());
            var items = body.GetProperty("items").EnumerateArray().ToArray();
            Assert.Equal(expected, items.Select(item => item.GetProperty("requestId").GetGuid()));
            Assert.All(items, item =>
            {
                Assert.Equal(6, item.EnumerateObject().Count());
                Assert.Equal(51, item.GetProperty("serviceId").GetInt32());
                Assert.Equal("Synthetic booked service", item.GetProperty("serviceLabel").GetString());
                Assert.Equal("SingleTooth", item.GetProperty("scope").GetString());
                Assert.Equal("85", item.GetProperty("toothCode").GetString());
                Assert.Equal(FixedClock.Utc, item.GetProperty("signedUtc").GetDateTimeOffset());
            });
        }
    }

    [Fact]
    public async Task Performed_HTTP_read_releases_no_page_when_audit_insert_or_commit_fails()
    {
        using var app = await CreateFixture(httpEnabled: true);
        using var doctor = await app.AsAsync(13);
        var key = Guid.NewGuid();
        var url = PerformedRoute(301);
        using var signed = await doctor.PostAsJsonAsync(url, SignRequest(key));
        Assert.Equal(HttpStatusCode.Created, signed.StatusCode);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER "SyntheticProcedureReadAuditFailure" BEFORE INSERT ON "ClinicalAuditEvents" BEGIN
                SELECT RAISE(ABORT, 'SyntheticProcedureReadAuditFailure');
            END;
            """);
        await CheckFailure(url);
        await CheckFailure(url + "?page=2&pageSize=1"); // An empty result may not bypass auditing.
        await db.Database.ExecuteSqlRawAsync("""DROP TRIGGER "SyntheticProcedureReadAuditFailure";""");
        app.CommitFailure.Enabled = true;
        try { await CheckFailure(url); }
        finally { app.CommitFailure.Enabled = false; }

        Assert.Equal(0, await db.Set<ClinicalAuditEvent>().CountAsync(row => row.Action == ClinicalAuditAction.HistoryRead));
        Assert.Equal(key, (await db.Set<PerformedProcedure>().SingleAsync()).RequestId);
        using var retry = await doctor.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(1, await db.Set<ClinicalAuditEvent>().CountAsync(row => row.Action == ClinicalAuditAction.HistoryRead));

        async Task CheckFailure(string path)
        {
            using var response = await doctor.GetAsync(path);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain(key.ToString(), body);
            Assert.DoesNotContain("Synthetic booked service", body);
            Assert.DoesNotContain("SyntheticProcedureReadAuditFailure", body);
            Assert.DoesNotContain("Synthetic performed procedure commit failure", body);
        }
    }

    [Fact]
    public async Task Patient_history_is_opt_in_bounded_to_same_patient_and_audits_released_visits()
    {
        using var app = await CreateFixture(httpEnabled: true);
        await SeedPatientHistory(app);
        using var doctor = await app.AsAsync(13);
        using var foreign = await app.AsAsync(23, clinicId: 202);
        using var patientRole = await app.AsAsync(11);
        using var otherDoctor = await app.AsAsync(17);
        using var anonymous = app.Client();
        foreach (var id in new[] { 301, 304, 305 })
        {
            using var signed = await doctor.PostAsJsonAsync(PerformedRoute(id), SignRequest(Guid.NewGuid()));
            Assert.Equal(HttpStatusCode.Created, signed.StatusCode);
        }
        using var anotherAuthor = await otherDoctor.PostAsJsonAsync(PerformedRoute(306), SignRequest(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Created, anotherAuthor.StatusCode);
        var url = HistoryRoute(301);
        using var off = await doctor.GetAsync(url);
        Assert.Equal(HttpStatusCode.NotFound, off.StatusCode);
        var config = app.Services.GetRequiredService<IConfiguration>();
        config["Features:PatientHistoryAcceptance"] = "true";
        config["Features:PerformedProcedureAcceptance"] = "false";
        using var dependencyOff = await doctor.GetAsync(url);
        Assert.Equal(HttpStatusCode.NotFound, dependencyOff.StatusCode);
        config["Features:PerformedProcedureAcceptance"] = "true";
        foreach (var (client, expected) in new[]
        {
            (foreign, HttpStatusCode.NotFound), (otherDoctor, HttpStatusCode.NotFound),
            (anonymous, HttpStatusCode.Unauthorized), (patientRole, HttpStatusCode.Forbidden)
        })
        {
            using var denied = await client.GetAsync(url);
            Assert.Equal(expected, denied.StatusCode);
        }
        foreach (var bad in new[] { "?from=2026-01-01&until=2026-11-27", "?from=2026-11-26", "?from=2026-11-26&until=2026-11-27&pageSize=21" })
        {
            using var invalid = await doctor.GetAsync(HistoryRoute(301, bad));
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        await CheckPage(1, [301], true);
        await CheckPage(2, [304], false);
        await CheckPage(3, [], false);

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var events = await db.Set<ClinicalAuditEvent>().Where(row => row.Action == ClinicalAuditAction.HistoryRead)
            .OrderBy(row => row.Id).ToArrayAsync();
        Assert.Equal(new[] { 301, 301, 304, 301 }, events.Select(row => row.AppointmentId));
        Assert.All(events, row => Assert.Equal((101, 13), (row.ClinicId, row.ActorUserId)));
        Assert.Equal(events[1].CorrelationId, events[2].CorrelationId);

        async Task CheckPage(int page, int[] expected, bool more)
        {
            using var response = await doctor.GetAsync(url + $"&page={page}&pageSize=1");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(4, json.EnumerateObject().Count());
            Assert.Equal(more, json.GetProperty("hasMore").GetBoolean());
            var items = json.GetProperty("items").EnumerateArray().ToArray();
            Assert.Equal(expected, items.Select(row => row.GetProperty("appointmentId").GetInt32()));
            Assert.All(items, row => Assert.Equal(2, row.EnumerateObject().Count()));
        }
    }

    [Fact]
    public async Task Failed_audit_on_a_nonanchor_history_visit_rolls_back_all_metadata_and_no_page_escapes()
    {
        using var app = await CreateFixture(httpEnabled: true);
        await SeedPatientHistory(app);
        app.Services.GetRequiredService<IConfiguration>()["Features:PatientHistoryAcceptance"] = "true";
        using var doctor = await app.AsAsync(13);
        using var signed = await doctor.PostAsJsonAsync(PerformedRoute(304), SignRequest(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Created, signed.StatusCode);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER "SyntheticPatientHistoryAuditFailure" BEFORE INSERT ON "ClinicalAuditEvents"
            FOR EACH ROW WHEN NEW."AppointmentId" = 304 BEGIN
                SELECT RAISE(ABORT, 'SyntheticPatientHistoryAuditFailure');
            END;
            """);
        using var blocked = await doctor.GetAsync(HistoryRoute(301));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, blocked.StatusCode);
        Assert.True(blocked.Headers.CacheControl?.NoStore);
        var body = await blocked.Content.ReadAsStringAsync();
        Assert.DoesNotContain("SyntheticPatientHistoryAuditFailure", body);
        Assert.DoesNotContain("startedUtc", body);
        Assert.DoesNotContain("items", body);
        Assert.Equal(0, await db.Set<ClinicalAuditEvent>().CountAsync(row => row.Action == ClinicalAuditAction.HistoryRead));
    }

    private static string HistoryRoute(int id, string query = "?from=2026-11-26&until=2026-11-27")
        => $"/api/clinical/appointments/{id}/patient-history" + query;

    private static async Task SeedPatientHistory(ProcedureFactory app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Patients.Add(new Patient { Id = 43, ClinicId = 101, FullName = "Synthetic other patient", Phone = "synthetic" });
        db.DoctorServices.Add(new DoctorService { ClinicId = 101, DoctorProfileId = 33, ServiceId = 51, DurationMinutes = 30 });
        await db.SaveChangesAsync();
        db.Appointments.Add(Encounter(304, FixedClock.Utc.AddHours(-3)));
        var unrelated = Encounter(305, FixedClock.Utc.AddHours(-4));
        unrelated.PatientId = 43;
        db.Appointments.Add(unrelated);
        var otherDoctor = Encounter(306, FixedClock.Utc.AddHours(-5));
        otherDoctor.DoctorProfileId = 33;
        db.Appointments.Add(otherDoctor);
        await db.SaveChangesAsync();
    }

    private static SignPerformedProcedureRequest SignRequest(Guid id) => new(id, 51, "SingleTooth", "85");
    private static string PerformedRoute(int appointmentId) => $"/api/clinical/appointments/{appointmentId}/performed-procedures";

    private static async Task AssertNoProcedureWrites(ProcedureFactory app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await db.Set<PerformedProcedure>().ToArrayAsync());
        Assert.Empty(await db.Set<ClinicalAuditEvent>().ToArrayAsync());
    }

    private static StructuredProcedureDraftInput Input(int serviceId = 52)
        => new(301, serviceId, StructuredTreatmentScope.SingleTooth, "85");

    private static ClaimsPrincipal Principal(int actor, int clinic, Role role)
        => new(new ClaimsIdentity(
            [new("userId", actor.ToString()), new("clinicId", clinic.ToString()), new(ClaimTypes.Role, role.ToString())],
            "SyntheticClinic"));

    private static async Task<ProcedureFactory> CreateFixture(bool httpEnabled = false, bool fakePersistentMetadata = false,
        Action<ProcedureFactory>? configure = null)
    {
        var app = new ProcedureFactory(httpEnabled, fakePersistentMetadata);
        configure?.Invoke(app);
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

    private sealed class ProcedureFactory(bool httpEnabled, bool fakePersistentMetadata) : DoctorApiFactory
    {
        internal bool UseLoopbackHost { get; set; }
        internal readonly FailingCommit CommitFailure = new();
        internal readonly RefusePersistentOpen OpenGuard = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            if (httpEnabled)
                builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                    new Dictionary<string, string?> { ["Features:PerformedProcedureAcceptance"] = "true" }));
            if (UseLoopbackHost)
                builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["AllowedHosts"] = "127.0.0.1",
                        ["Notifications:WorkerEnabled"] = "false"
                    }));
            builder.ConfigureServices(services =>
            {
                services.AddDbContext<AppDbContext>(options => options.AddInterceptors(CommitFailure, OpenGuard));
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FixedClock());
                if (fakePersistentMetadata)
                    services.AddControllers(options => options.Filters.Add(new SwapConnectionMetadata()));
            });
        }
    }

    private sealed class FixedClock : TimeProvider
    {
        internal static DateTimeOffset Utc { get; } = new(2026, 11, 27, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Utc;
    }

    // Run only after real clinic JWT authentication: present file-shaped connection
    // metadata to the MVC action without ever opening or creating a file database.
    private sealed class SwapConnectionMetadata : IAsyncActionFilter
    {
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            if (context.Controller is not (PerformedProcedureAcceptanceController or PatientHistoryAcceptanceController or AssignedEncounterAcceptanceController))
            {
                await next();
                return;
            }
            var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
            var memory = db.Database.GetConnectionString();
            db.Database.SetConnectionString("Data Source=synthetic-never-open-acceptance.db;Mode=ReadOnly;Pooling=False");
            try { await next(); }
            finally { db.Database.SetConnectionString(memory); }
        }
    }

    private sealed class RefusePersistentOpen : DbConnectionInterceptor
    {
        internal int PersistentAttempts;

        private void Verify(DbConnection connection)
        {
            if (connection is not SqliteConnection ||
                EncounterTreatmentController.IsMemorySqliteConnection(connection.ConnectionString))
                return;
            Interlocked.Increment(ref PersistentAttempts);
            throw new InvalidOperationException("Test fixture may never open a persistent SQLite database.");
        }

        public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData,
            InterceptionResult result)
        {
            Verify(connection);
            return result;
        }

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection,
            ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            Verify(connection);
            return ValueTask.FromResult(result);
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
