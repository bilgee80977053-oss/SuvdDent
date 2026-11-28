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
        var key = Guid.NewGuid();
        using var amendment = await doctor.PostAsJsonAsync(AddendaRoute(301), new { requestId = key, note = Note });
        using var amendmentRead = await doctor.GetAsync(AddendaRoute(301) + "/" + key);
        Assert.Equal(HttpStatusCode.NotFound, amendment.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, amendmentRead.StatusCode);
        using var list = await doctor.GetAsync(AddendaRoute(301));
        Assert.Equal(HttpStatusCode.NotFound, list.StatusCode);
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

    [Fact]
    public async Task Addenda_preserve_original_and_exact_replay_does_not_add_an_event()
    {
        using var app = await App(enabled: true);
        using var doctor = await app.AsAsync(13);
        using var foreign = await app.AsAsync(23, clinicId: 202);
        using var otherDoctor = await app.AsAsync(17);
        const int id = 301;
        var key = Guid.NewGuid();
        const string first = "Synthetic addendum, first";
        const string second = "Synthetic addendum, second";

        using var noOriginal = await doctor.PostAsJsonAsync(AddendaRoute(id), new { requestId = key, note = first });
        Assert.Equal(HttpStatusCode.NotFound, noOriginal.StatusCode);
        using var signed = await doctor.PostAsJsonAsync(Route(id), new { note = Note });
        Assert.Equal(HttpStatusCode.Created, signed.StatusCode);

        foreach (var invalid in new[]
        {
            new { requestId = Guid.Empty, note = first },
            new { requestId = key, note = new string('a', 2001) },
            new { requestId = key, note = "  " }
        })
        {
            using var response = await doctor.PostAsJsonAsync(AddendaRoute(id), invalid);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        using var created = await doctor.PostAsJsonAsync(AddendaRoute(id), new { requestId = key, note = " " + first + " " });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(AddendaRoute(id) + "/" + key, created.Headers.Location?.AbsolutePath);
        Assert.True(created.Headers.CacheControl?.NoStore);
        Assert.DoesNotContain(first, await created.Content.ReadAsStringAsync());
        using var replayed = await doctor.PostAsJsonAsync(AddendaRoute(id), new { requestId = key, note = first });
        Assert.Equal(HttpStatusCode.NoContent, replayed.StatusCode);
        using var mismatch = await doctor.PostAsJsonAsync(AddendaRoute(id), new { requestId = key, note = second });
        Assert.Equal(HttpStatusCode.Conflict, mismatch.StatusCode);

        var secondKey = Guid.NewGuid();
        using var appended = await doctor.PostAsJsonAsync(AddendaRoute(id), new { requestId = secondKey, note = second });
        Assert.Equal(HttpStatusCode.Created, appended.StatusCode);
        foreach (var denied in new[] { foreign, otherDoctor })
        {
            using var deniedWrite = await denied.PostAsJsonAsync(AddendaRoute(id), new { requestId = Guid.NewGuid(), note = second });
            using var deniedRead = await denied.GetAsync(AddendaRoute(id) + "/" + key);
            Assert.Equal(HttpStatusCode.NotFound, deniedWrite.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, deniedRead.StatusCode);
        }
        using var wrongEncounter = await doctor.GetAsync(AddendaRoute(305) + "/" + key);
        Assert.Equal(HttpStatusCode.NotFound, wrongEncounter.StatusCode);
        using var read = await doctor.GetAsync(AddendaRoute(id) + "/" + key);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.True(read.Headers.CacheControl?.NoStore);
        var responseJson = await read.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(first, responseJson.GetProperty("note").GetString());
        Assert.Equal(key, responseJson.GetProperty("requestId").GetGuid());
        Assert.Equal(3, responseJson.EnumerateObject().Count());

        await InDb(app, async db =>
        {
            Assert.Equal(Note, (await db.Set<TreatmentRecord>().SingleAsync()).Note);
            var rows = await db.Set<TreatmentAddendum>().AsNoTracking().ToArrayAsync();
            Assert.Equal(2, rows.Length);
            Assert.Equal(first, Assert.Single(rows, row => row.RequestId == key).Note);
            Assert.Equal(second, Assert.Single(rows, row => row.RequestId == secondKey).Note);
            var audit = await db.Set<ClinicalAuditEvent>().AsNoTracking().OrderBy(row => row.Id).ToArrayAsync();
            Assert.Equal(new[] { ClinicalAuditAction.TreatmentRecorded, ClinicalAuditAction.TreatmentAmended,
                ClinicalAuditAction.TreatmentAmended, ClinicalAuditAction.HistoryRead }, audit.Select(row => row.Action));
            Assert.All(audit, row => Assert.Equal((101, 301, 13), (row.ClinicId, row.AppointmentId, row.ActorUserId)));
            Assert.DoesNotContain(audit, row => row.CorrelationId == key || row.CorrelationId == secondKey);
        });
    }

    [Fact]
    public async Task Addendum_commit_failure_rolls_back_amendment_event_and_failed_read_audit_releases_no_text()
    {
        using var app = await App(enabled: true);
        using var doctor = await app.AsAsync(13);
        using var signed = await doctor.PostAsJsonAsync(Route(301), new { note = Note });
        Assert.Equal(HttpStatusCode.Created, signed.StatusCode);
        const string amended = "Synthetic amendment text";
        var requestId = Guid.NewGuid();
        var route = AddendaRoute(301);
        app.CommitFailure.Enabled = true;
        using var failedWrite = await doctor.PostAsJsonAsync(route, new { requestId, note = amended });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failedWrite.StatusCode);
        Assert.DoesNotContain(amended, await failedWrite.Content.ReadAsStringAsync());
        app.CommitFailure.Enabled = false;
        await InDb(app, async db =>
        {
            Assert.Empty(await db.Set<TreatmentAddendum>().ToListAsync());
            Assert.Equal(Note, (await db.Set<TreatmentRecord>().SingleAsync()).Note);
            Assert.Equal(ClinicalAuditAction.TreatmentRecorded, (await db.Set<ClinicalAuditEvent>().SingleAsync()).Action);
        });

        using var retry = await doctor.PostAsJsonAsync(route, new { requestId, note = amended });
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        await InDb(app, db => db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER "SyntheticAddendumAuditFailure" BEFORE INSERT ON "ClinicalAuditEvents" BEGIN
                SELECT RAISE(ABORT, 'SyntheticAddendumAuditFailure');
            END;
            """));
        using var deniedRead = await doctor.GetAsync(route + "/" + requestId);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, deniedRead.StatusCode);
        Assert.True(deniedRead.Headers.CacheControl?.NoStore);
        var body = await deniedRead.Content.ReadAsStringAsync();
        Assert.DoesNotContain(amended, body);
        Assert.DoesNotContain("SyntheticAddendumAuditFailure", body);
        await InDb(app, async db =>
        {
            Assert.Equal(amended, (await db.Set<TreatmentAddendum>().SingleAsync()).Note);
            Assert.Equal(0, await db.Set<ClinicalAuditEvent>().CountAsync(row => row.Action == ClinicalAuditAction.HistoryRead));
        });
    }

    [Fact]
    public async Task Sqlite_refuses_addendum_mutation_replacement_and_wrong_signer()
    {
        using var app = await App(enabled: true);
        using var doctor = await app.AsAsync(13);
        using var signed = await doctor.PostAsJsonAsync(Route(301), new { note = Note });
        Assert.Equal(HttpStatusCode.Created, signed.StatusCode);
        var key = Guid.NewGuid();
        using var created = await doctor.PostAsJsonAsync(AddendaRoute(301), new { requestId = key, note = "Synthetic addendum" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        await InDb(app, async db =>
        {
            db.Add(new TreatmentAddendum { ClinicId = 101, AppointmentId = 301, RequestId = Guid.NewGuid(),
                AuthorUserId = 17, Note = "Synthetic unassigned", SignedUtc = Now });
            var wrongAuthor = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal(1811, Assert.IsType<SqliteException>(wrongAuthor.InnerException).SqliteExtendedErrorCode);
            db.ChangeTracker.Clear();

            await db.Database.OpenConnectionAsync();
            await db.Database.ExecuteSqlRawAsync("PRAGMA recursive_triggers=OFF;");
            foreach (var sql in new[]
            {
                "UPDATE TreatmentAddenda SET Note = 'Synthetic update';",
                "DELETE FROM TreatmentAddenda;",
                "INSERT OR REPLACE INTO TreatmentAddenda SELECT * FROM TreatmentAddenda;"
            })
            {
                var error = await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync(sql));
                Assert.Equal(1811, error.SqliteExtendedErrorCode);
            }
            const string lowerCaseKey = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
            var noncanonical = await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO TreatmentAddenda (ClinicId, AppointmentId, RequestId, AuthorUserId, Note, SignedUtc)
                VALUES (101, 301, {lowerCaseKey}, 13, 'Synthetic noncanonical request', {Now.Ticks})
                """));
            Assert.Equal(275, noncanonical.SqliteExtendedErrorCode);
            Assert.Equal(key, (await db.Set<TreatmentAddendum>().AsNoTracking().SingleAsync()).RequestId);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Addenda_migration_is_additive_for_each_provider(bool postgres)
    {
        using AppDbContext db = postgres ? new PostgreSqlAppDbContextDesignTimeFactory().CreateDbContext([])
            : new AppDbContextDesignTimeFactory().CreateDbContext([]);
        var ids = db.Database.GetMigrations().ToArray();
        var target = ids.Single(id => id.EndsWith(postgres ? "_AddPostgreSqlTreatmentAddenda" :
            "_AddTreatmentAddenda", StringComparison.Ordinal));
        var preceding = ids[Array.IndexOf(ids, target) - 1];
        Assert.EndsWith("TreatmentRecords", preceding);
        var assembly = db.GetService<IMigrationsAssembly>();
        var migration = assembly.CreateMigration(assembly.Migrations[target], db.Database.ProviderName!);
        Assert.All(migration.UpOperations, op => Assert.True(op is
            CreateTableOperation { Name: "TreatmentAddenda" } or CreateIndexOperation { Table: "TreatmentAddenda" } or SqlOperation));
        var table = Assert.Single(migration.UpOperations.OfType<CreateTableOperation>());
        Assert.Equal(new[] { "ClinicId", "AppointmentId", "RequestId" }, table.PrimaryKey!.Columns);
        var recordKey = Assert.Single(table.ForeignKeys, fk => fk.PrincipalTable == "TreatmentRecords");
        Assert.Equal(new[] { "ClinicId", "AppointmentId" }, recordKey.Columns);
        Assert.Equal(recordKey.Columns, recordKey.PrincipalColumns);
        Assert.Equal(ReferentialAction.Restrict, recordKey.OnDelete);
        var sql = db.GetService<IMigrator>().GenerateScript(preceding, target, postgres
            ? MigrationsSqlGenerationOptions.Idempotent : MigrationsSqlGenerationOptions.Default);
        Assert.DoesNotContain("ALTER TABLE", sql);
        Assert.DoesNotContain("DROP TABLE", sql);
        Assert.Contains("TR_TreatmentAddenda_RequireOriginalSigner", sql);
        Assert.Contains(postgres ? "BEFORE TRUNCATE ON" : "TR_TreatmentAddenda_NoReplace", sql);
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal(ConnectionState.Closed, db.Database.GetDbConnection().State);
    }

    [Fact]
    public async Task Addenda_discovery_is_bounded_metadata_only_deterministic_and_encounter_scoped()
    {
        using var app = await App(enabled: true);
        using var doctor = await app.AsAsync(13);
        using var foreign = await app.AsAsync(23, clinicId: 202);
        using var unassigned = await app.AsAsync(17);
        var url = AddendaRoute(301);
        using var signed = await doctor.PostAsJsonAsync(Route(301), new { note = Note });
        Assert.Equal(HttpStatusCode.Created, signed.StatusCode);
        var keys = new[]
        {
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            Guid.Parse("99999999-9999-9999-9999-999999999999")
        };
        foreach (var key in keys.Reverse())
        {
            using var added = await doctor.PostAsJsonAsync(url, new { requestId = key, note = "Synthetic hidden addendum" });
            Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        }

        foreach (var denied in new[] { foreign, unassigned })
        {
            using var rejected = await denied.GetAsync(url);
            Assert.Equal(HttpStatusCode.NotFound, rejected.StatusCode);
        }
        using var wrongEncounter = await doctor.GetAsync(AddendaRoute(305));
        Assert.Equal(HttpStatusCode.NotFound, wrongEncounter.StatusCode);
        foreach (var invalid in new[] { "?page=0", "?page=1001", "?pageSize=0", "?pageSize=51" })
        {
            using var rejected = await doctor.GetAsync(url + invalid);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }
        await Review(1, keys[..2], true);
        await Review(2, keys[2..], false);
        await Review(3, [], false);
        await Review(1000, [], false);
        await InDb(app, async db =>
        {
            Assert.Equal(Note, (await db.Set<TreatmentRecord>().SingleAsync()).Note);
            Assert.Equal(3, await db.Set<TreatmentAddendum>().CountAsync());
            var reads = await db.Set<ClinicalAuditEvent>().Where(row => row.Action == ClinicalAuditAction.HistoryRead).ToArrayAsync();
            Assert.Equal(4, reads.Length);
            Assert.All(reads, row => Assert.Equal((101, 301, 13), (row.ClinicId, row.AppointmentId, row.ActorUserId)));
        });

        async Task Review(int page, Guid[] expected, bool hasMore)
        {
            using var response = await doctor.GetAsync(url + $"?page={page}&pageSize=2");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("Synthetic hidden addendum", body);
            var json = JsonSerializer.Deserialize<JsonElement>(body);
            Assert.Equal(page, json.GetProperty("page").GetInt32());
            Assert.Equal(2, json.GetProperty("pageSize").GetInt32());
            Assert.Equal(hasMore, json.GetProperty("hasMore").GetBoolean());
            var items = json.GetProperty("items").EnumerateArray().ToArray();
            Assert.Equal(expected, items.Select(item => item.GetProperty("requestId").GetGuid()));
            Assert.All(items, item =>
            {
                Assert.Equal(2, item.EnumerateObject().Count());
                Assert.Equal(Now, item.GetProperty("signedUtc").GetDateTimeOffset());
            });
        }
    }

    [Fact]
    public async Task Addenda_discovery_returns_no_reference_if_its_audit_is_not_durable()
    {
        using var app = await App(enabled: true);
        using var doctor = await app.AsAsync(13);
        var key = Guid.NewGuid();
        var url = AddendaRoute(301);
        using var signed = await doctor.PostAsJsonAsync(Route(301), new { note = Note });
        Assert.Equal(HttpStatusCode.Created, signed.StatusCode);
        using var added = await doctor.PostAsJsonAsync(url, new { requestId = key, note = "Synthetic hidden addendum" });
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        await InDb(app, db => db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER "SyntheticDiscoveryAuditFailure" BEFORE INSERT ON "ClinicalAuditEvents" BEGIN
                SELECT RAISE(ABORT, 'SyntheticDiscoveryAuditFailure');
            END;
            """));

        using var read = await doctor.GetAsync(url);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, read.StatusCode);
        Assert.True(read.Headers.CacheControl?.NoStore);
        var body = await read.Content.ReadAsStringAsync();
        Assert.DoesNotContain(key.ToString(), body);
        Assert.DoesNotContain("Synthetic hidden addendum", body);
        Assert.DoesNotContain("SyntheticDiscoveryAuditFailure", body);
        await InDb(app, async db =>
        {
            Assert.Equal(key, (await db.Set<TreatmentAddendum>().SingleAsync()).RequestId);
            Assert.Equal(0, await db.Set<ClinicalAuditEvent>().CountAsync(row => row.Action == ClinicalAuditAction.HistoryRead));
        });
    }

    private static string AddendaRoute(int appointmentId) => Route(appointmentId) + "/addenda";

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
