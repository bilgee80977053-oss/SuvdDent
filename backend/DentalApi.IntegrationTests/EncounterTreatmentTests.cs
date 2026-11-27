using System.Data;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DentalApi.Controllers;
using DentalApi.Data;
using DentalApi.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DentalApi.IntegrationTests;

public sealed class EncounterTreatmentTests
{
    private const string Note = "Synthetic finding\nSynthetic plan";
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 0, 0, 0, TimeSpan.Zero);
    private static string Route(int id) => $"/api/clinical/appointments/{id}/treatment";

    [Theory]
    [InlineData("Data Source=:memory:", true)]
    [InlineData("Data Source=synthetic-test;Mode=Memory;Cache=Shared", true)]
    [InlineData("Data Source=synthetic-persistent.db", false)]
    [InlineData("Data Source=synthetic-persistent.db;Mode=ReadOnly", false)]
    [InlineData("Host=localhost;Database=synthetic", false)]
    [InlineData(null, false)]
    public void Draft_target_check_refuses_persistent_or_invalid_configuration(string? connection, bool expected)
        => Assert.Equal(expected, EncounterTreatmentController.IsMemorySqliteConnection(connection));

    [Fact]
    public async Task Default_off_hides_the_route_from_an_assigned_clinician()
    {
        using var app = await App();
        using var doctor = await app.AsAsync(13);
        using var read = await doctor.GetAsync(Route(301));
        using var write = await doctor.PostAsJsonAsync(Route(301), new { note = Note });
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, write.StatusCode);
        await InDb(app, async db =>
        {
            Assert.Empty(await db.Set<TreatmentRecord>().ToListAsync());
            Assert.Empty(await db.Set<ClinicalAuditEvent>().ToListAsync());
        });
    }

    [Fact]
    public async Task Authentication_and_doctor_role_are_mandatory()
    {
        using var app = await App(enabled: true);
        using var anonymous = app.Client();
        await CheckBoth(anonymous, 301, HttpStatusCode.Unauthorized);
        foreach (var id in new[] { 11, 15, 16 })
        {
            using var wrongRole = await app.AsAsync(id);
            await CheckBoth(wrongRole, 301, HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task Only_assigned_active_clinician_can_sign_and_read_one_bounded_audited_note()
    {
        using var app = await App(enabled: true);
        using var foreign = await app.AsAsync(23, clinicId: 202);
        await CheckBoth(foreign, 301, HttpStatusCode.NotFound);
        using var unassigned = await app.AsAsync(17);
        await CheckBoth(unassigned, 301, HttpStatusCode.NotFound);

        using var doctor = await app.AsAsync(13);
        await CheckBoth(doctor, 302, HttpStatusCode.NotFound); // cancelled
        await CheckBoth(doctor, 303, HttpStatusCode.NotFound); // future
        using var notCreated = await doctor.GetAsync(Route(301));
        Assert.Equal(HttpStatusCode.NotFound, notCreated.StatusCode);

        foreach (var invalid in new[] { "", "    ", new string('a', 2001), "Synthetic\0control" })
        {
            using var response = await doctor.PostAsJsonAsync(Route(301), new { note = invalid });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        using var identityOverride = await doctor.PostAsJsonAsync(Route(301),
            new { note = Note, patientId = 42, clinicId = 202, authorUserId = 23 });
        Assert.Equal(HttpStatusCode.BadRequest, identityOverride.StatusCode);

        using var created = await doctor.PostAsJsonAsync(Route(301),
            new { note = "  Synthetic finding\r\nSynthetic plan  " });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.True(created.Headers.CacheControl?.NoStore);
        Assert.Equal(Route(301), created.Headers.Location?.AbsolutePath);
        Assert.DoesNotContain("Synthetic finding", await created.Content.ReadAsStringAsync());
        await CheckBoth(foreign, 301, HttpStatusCode.NotFound);
        await CheckBoth(unassigned, 301, HttpStatusCode.NotFound);

        using var duplicate = await doctor.PostAsJsonAsync(Route(301), new { note = "Synthetic changed" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        using var read = await doctor.GetAsync(Route(301));
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.True(read.Headers.CacheControl?.NoStore);
        var json = await read.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(Note, json.GetProperty("note").GetString());
        Assert.Equal(301, json.GetProperty("appointmentId").GetInt32());
        Assert.Equal(Now, json.GetProperty("signedUtc").GetDateTimeOffset());
        Assert.Equal(3, json.EnumerateObject().Count());

        await InDb(app, async db =>
        {
            (await db.DoctorProfiles.FindAsync(31))!.IsActive = false;
            await db.SaveChangesAsync();
        });
        await CheckBoth(doctor, 301, HttpStatusCode.NotFound);
        await InDb(app, async db =>
        {
            var row = await db.Set<TreatmentRecord>().AsNoTracking().SingleAsync();
            Assert.Equal((101, 301, 13), (row.ClinicId, row.AppointmentId, row.AuthorUserId));
            var events = await db.Set<ClinicalAuditEvent>().AsNoTracking().OrderBy(a => a.Id).ToArrayAsync();
            Assert.Equal(new[] { ClinicalAuditAction.TreatmentRecorded, ClinicalAuditAction.HistoryRead },
                events.Select(a => a.Action));
            Assert.All(events, a =>
            {
                Assert.Equal((101, 301, 13), (a.ClinicId, a.AppointmentId, a.ActorUserId));
                Assert.NotEqual(Guid.Empty, a.CorrelationId);
            });
            Assert.NotEqual(events[0].CorrelationId, events[1].CorrelationId);
        });
    }

    [Fact]
    public async Task Audit_failure_rolls_back_signing_and_returns_no_note_on_read()
    {
        using var app = await App(enabled: true);
        using var doctor = await app.AsAsync(13);
        await InDb(app, AddFailure);

        using var failedWrite = await doctor.PostAsJsonAsync(Route(301), new { note = Note });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failedWrite.StatusCode);
        Assert.True(failedWrite.Headers.CacheControl?.NoStore);
        Assert.DoesNotContain("Synthetic finding", await failedWrite.Content.ReadAsStringAsync());
        await InDb(app, async db =>
        {
            Assert.Empty(await db.Set<TreatmentRecord>().ToListAsync());
            Assert.Empty(await db.Set<ClinicalAuditEvent>().ToListAsync());
            await db.Database.ExecuteSqlRawAsync("""DROP TRIGGER "SyntheticAuditFailure";""");
        });

        using var signed = await doctor.PostAsJsonAsync(Route(301), new { note = Note });
        Assert.Equal(HttpStatusCode.Created, signed.StatusCode);
        await InDb(app, AddFailure);
        using var failedRead = await doctor.GetAsync(Route(301));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failedRead.StatusCode);
        Assert.True(failedRead.Headers.CacheControl?.NoStore);
        var body = await failedRead.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Synthetic finding", body);
        Assert.DoesNotContain("SyntheticAuditFailure", body);
        await InDb(app, async db =>
        {
            Assert.Equal(Note, (await db.Set<TreatmentRecord>().SingleAsync()).Note);
            Assert.Equal(ClinicalAuditAction.TreatmentRecorded, (await db.Set<ClinicalAuditEvent>().SingleAsync()).Action);
        });

        static Task AddFailure(AppDbContext db) => db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER "SyntheticAuditFailure" BEFORE INSERT ON "ClinicalAuditEvents" BEGIN
                SELECT RAISE(ABORT, 'SyntheticAuditFailure');
            END;
            """);
    }

    [Fact]
    public async Task Commit_failure_discards_the_saved_note_and_its_audit_event()
    {
        using var app = await App(enabled: true);
        using var doctor = await app.AsAsync(13);
        app.CommitFailure.Enabled = true;
        using var failed = await doctor.PostAsJsonAsync(Route(301), new { note = Note });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.DoesNotContain("Synthetic finding", await failed.Content.ReadAsStringAsync());
        app.CommitFailure.Enabled = false;

        await InDb(app, async db =>
        {
            Assert.Empty(await db.Set<TreatmentRecord>().ToListAsync());
            Assert.Empty(await db.Set<ClinicalAuditEvent>().ToListAsync());
        });
        using var retry = await doctor.PostAsJsonAsync(Route(301), new { note = Note });
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
    }

    [Fact]
    public async Task Sqlite_database_blocks_wrong_signer_and_modification_of_the_note()
    {
        using var app = await App(enabled: true);
        using var doctor = await app.AsAsync(13);
        using var signed = await doctor.PostAsJsonAsync(Route(301), new { note = Note });
        Assert.Equal(HttpStatusCode.Created, signed.StatusCode);
        await InDb(app, async db =>
        {
            db.Add(new TreatmentRecord
            {
                ClinicId = 101, AppointmentId = 305, AuthorUserId = 17, Note = "Synthetic invalid signer", SignedUtc = Now
            });
            var failure = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal(1811, Assert.IsType<SqliteException>(failure.InnerException).SqliteExtendedErrorCode);
            db.ChangeTracker.Clear();

            await db.Database.OpenConnectionAsync();
            await db.Database.ExecuteSqlRawAsync("PRAGMA recursive_triggers=OFF;");
            foreach (var sql in new[]
            {
                "UPDATE TreatmentRecords SET Note = 'Synthetic update';",
                "DELETE FROM TreatmentRecords;",
                "INSERT OR REPLACE INTO TreatmentRecords SELECT * FROM TreatmentRecords;"
            })
            {
                var error = await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync(sql));
                Assert.Equal(1811, error.SqliteExtendedErrorCode);
            }
            Assert.Equal(Note, (await db.Set<TreatmentRecord>().AsNoTracking().SingleAsync()).Note);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Provider_migration_is_additive_and_matches_snapshot(bool postgres)
    {
        using AppDbContext db = postgres ? new PostgreSqlAppDbContextDesignTimeFactory().CreateDbContext([])
            : new AppDbContextDesignTimeFactory().CreateDbContext([]);
        var ids = db.Database.GetMigrations().ToArray();
        var expected = postgres ? "_AddPostgreSqlTreatmentRecords" : "_AddTreatmentRecords";
        var target = ids.Single(x => x.EndsWith(expected, StringComparison.Ordinal));
        var before = ids[Array.IndexOf(ids, target) - 1];
        Assert.EndsWith("ClinicalAuditTrail", before);
        var assembly = db.GetService<IMigrationsAssembly>();
        var migration = assembly.CreateMigration(assembly.Migrations[target], db.Database.ProviderName!);
        Assert.All(migration.UpOperations, op => Assert.True(op is
            CreateTableOperation { Name: "TreatmentRecords" } or CreateIndexOperation { Table: "TreatmentRecords" } or SqlOperation));
        var table = Assert.Single(migration.UpOperations.OfType<CreateTableOperation>());
        Assert.Equal(new[] { "ClinicId", "AppointmentId" }, table.PrimaryKey!.Columns);
        Assert.Equal(2, table.ForeignKeys.Count);
        Assert.All(table.ForeignKeys, fk =>
        {
            Assert.Equal("ClinicId", fk.Columns[0]);
            Assert.Equal(new[] { "ClinicId", "Id" }, fk.PrincipalColumns);
            Assert.Equal(ReferentialAction.Restrict, fk.OnDelete);
        });
        var sql = db.GetService<IMigrator>().GenerateScript(before, target, postgres
            ? MigrationsSqlGenerationOptions.Idempotent : MigrationsSqlGenerationOptions.Default);
        Assert.DoesNotContain("DROP TABLE", sql);
        Assert.DoesNotContain("ALTER TABLE", sql);
        Assert.DoesNotContain("ef_temp_", sql);
        Assert.Contains("TR_TreatmentRecords_RequireAssignedSigner", sql);
        Assert.Contains(postgres ? "BEFORE TRUNCATE ON" : "TR_TreatmentRecords_NoReplace", sql);
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal(ConnectionState.Closed, db.Database.GetDbConnection().State);
    }

    private static async Task CheckBoth(HttpClient client, int id, HttpStatusCode status)
    {
        using var read = await client.GetAsync(Route(id));
        using var write = await client.PostAsJsonAsync(Route(id), new { note = Note });
        Assert.Equal(status, read.StatusCode);
        Assert.Equal(status, write.StatusCode);
    }

    private static async Task<Factory> App(bool enabled = false)
    {
        var app = new Factory(enabled);
        await app.SeedAsync(); // This factory verifies Mode=Memory before migrating.
        await InDb(app, async db =>
        {
            db.Services.Add(new Service { Id = 51, ClinicId = 101, Name = "Synthetic service" });
            await db.SaveChangesAsync();
            db.DoctorServices.Add(new DoctorService { ClinicId = 101, DoctorProfileId = 31, ServiceId = 51,
                DurationMinutes = 30 });
            await db.SaveChangesAsync();
            db.Appointments.AddRange(
                AppointmentAt(301, Now.AddHours(-1)),
                AppointmentAt(302, Now.AddHours(-2), AppointmentStatus.Cancelled),
                AppointmentAt(303, Now.AddHours(2)),
                AppointmentAt(305, Now.AddHours(-3)));
            await db.SaveChangesAsync();
        });
        return app;

        static Appointment AppointmentAt(int id, DateTimeOffset start, AppointmentStatus status = AppointmentStatus.Confirmed)
            => new()
            {
                Id = id, ClinicId = 101, PatientId = 41, DoctorProfileId = 31, ServiceId = 51,
                StartUtc = start, EndUtc = start.AddMinutes(30), OccupiedStartUtc = start, OccupiedEndUtc = start.AddMinutes(30),
                DurationMinutesSnapshot = 30, ClinicTimeZoneIdSnapshot = "Asia/Ulaanbaatar", RequestId = Guid.NewGuid(), Status = status
            };
    }

    private static async Task InDb(Factory app, Func<AppDbContext, Task> action)
    {
        using var scope = app.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private sealed class Factory(bool enabled) : DoctorApiFactory
    {
        internal readonly FailingCommit CommitFailure = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            if (enabled)
                builder.ConfigureAppConfiguration((_, config) =>
                    config.AddInMemoryCollection(new Dictionary<string, string?> { ["Features:ClinicalTreatment"] = "true" }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FixedClock());
                services.AddDbContext<AppDbContext>(options => options.AddInterceptors(CommitFailure));
            });
        }
    }

    private sealed class FailingCommit : DbTransactionInterceptor
    {
        internal bool Enabled;

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (Enabled) throw new SqliteException("Synthetic commit failure", 5);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
