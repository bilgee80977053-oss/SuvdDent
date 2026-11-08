using System.Net;
using System.Net.Http.Json;
using DentalApi.Contracts;
using DentalApi.Data;
using DentalApi.Models;
using DentalApi.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Fixture = DentalApi.IntegrationTests.AppointmentTests.Fixture;

namespace DentalApi.IntegrationTests;

public sealed class NotificationTests
{
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
    private sealed class FailingProvider : IDemoAppointmentNotificationProvider
    {
        public Task<string> GenerateAsync(DemoConfirmation confirmation, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Synthetic private exception: must not be stored.");
    }
    private static AppointmentNotificationProcessor Processor(AppDbContext db, TimeProvider? clock = null,
        IDemoAppointmentNotificationProvider? provider = null) => new(db,
            provider ?? new DemoAppointmentNotificationProvider(), clock ?? TimeProvider.System);

    [Fact]
    public async Task Booking_records_one_email_and_sms_atomically_with_snapshot_content_and_replay_does_not_duplicate()
    {
        using var f = new Fixture(); await f.Seed(); using var client = await f.Account();
        var options = await f.Options(client); var key = Guid.NewGuid();
        using var first = await f.Book(client, f.Request(options), key);
        using var replay = await f.Book(client, f.Request(options), key);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode); Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        await f.Mutate(async db =>
        {
            var appointment = await db.Appointments.SingleAsync();
            var rows = await db.AppointmentNotificationOutbox.OrderBy(x => x.Channel).ToArrayAsync();
            Assert.Equal(new[] { "Email", "Sms" }, rows.Select(x => x.Channel));
            Assert.All(rows, x => { Assert.Equal(appointment.Id, x.AppointmentId); Assert.Equal(101, x.ClinicId);
                Assert.Equal(1, x.PlatformAccountId); Assert.Equal("Demo", x.Mode); Assert.Equal("Pending", x.Status);
                Assert.Contains($"NX-{appointment.Id}", x.Body); Assert.Contains("Asia/Ulaanbaatar", x.Body);
                Assert.Contains("09:10 +08:00", x.Body); Assert.DoesNotContain("account1@", x.Body); });
            Assert.Contains(appointment.DoctorNameSnapshot, rows[0].Body);
            Assert.Contains(appointment.ServiceNameSnapshot, rows[0].Body);
            Assert.StartsWith("[DEMO]", rows[0].Subject);
            Assert.Equal(2, await Processor(db).ProcessBatchAsync());
            Assert.Equal(0, await Processor(db).ProcessBatchAsync());
            db.ChangeTracker.Clear();
            Assert.All(await db.AppointmentNotificationOutbox.ToArrayAsync(), x => Assert.Equal("DemoGenerated", x.Status));
        });
    }

    [Fact]
    public async Task Failed_booking_has_no_intents_and_outbox_insert_failure_rolls_back_entire_booking()
    {
        using var f = new Fixture(); await f.Seed(); using var client = await f.Account(); var o = await f.Options(client);
        using var denied = await f.Book(client, f.Request(o, consent: false));
        Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
        await f.Mutate(async db =>
        {
            Assert.Empty(await db.AppointmentNotificationOutbox.ToArrayAsync());
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER SyntheticRejectIntent BEFORE INSERT ON AppointmentNotificationOutbox WHEN NEW.Channel='Sms' BEGIN SELECT RAISE(ABORT,'synthetic'); END;");
        });
        var key = Guid.NewGuid();
        using var failed = await f.Book(client, f.Request(o), key);
        Assert.Equal(HttpStatusCode.Conflict, failed.StatusCode);
        await f.Mutate(async db =>
        {
            Assert.Empty(await db.Appointments.ToArrayAsync()); Assert.Empty(await db.AppointmentNotificationOutbox.ToArrayAsync());
            Assert.Empty(await db.ClinicPatientLinks.ToArrayAsync()); Assert.Equal(2, await db.Patients.CountAsync());
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER SyntheticRejectIntent;");
        });
        using var retried = await f.Book(client, f.Request(o), key);
        Assert.Equal(HttpStatusCode.Created, retried.StatusCode);
        await f.Mutate(async db => Assert.Equal(2, await db.AppointmentNotificationOutbox.CountAsync()));
    }

    [Fact]
    public async Task Provider_failure_preserves_committed_booking_and_safe_retry_exhaustion()
    {
        using var f = new Fixture(); await f.Seed(); using var client = await f.Account(); var o = await f.Options(client);
        var key = Guid.NewGuid(); using var booked = await f.Book(client, f.Request(o), key);
        Assert.Equal(HttpStatusCode.Created, booked.StatusCode);
        var now = DateTimeOffset.UtcNow;
        await f.Mutate(async db =>
        {
            for (var i = 0; i < 3; i++)
                Assert.Equal(0, await Processor(db, new Clock(now.AddMinutes(i * 2)), new FailingProvider()).ProcessBatchAsync());
            Assert.Equal(1, await db.Appointments.CountAsync());
            var rows = await db.AppointmentNotificationOutbox.AsNoTracking().ToArrayAsync();
            Assert.All(rows, x => { Assert.Equal("Failed", x.Status); Assert.Equal(3, x.AttemptCount);
                Assert.Equal("DEMO_PROVIDER_FAILURE", x.LastErrorCode); Assert.Null(x.LeaseToken); });
        });
        using var replay = await f.Book(client, f.Request(o), key); Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(await booked.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Outbox_cannot_be_reassigned_to_other_account_or_clinic_and_API_never_returns_content()
    {
        using var f = new Fixture(); await f.Seed(); using var a = await f.Account(); using var b = await f.Account(2);
        using var first = await f.Book(a, f.Request(await f.Options(a)));
        using var second = await f.Book(b, f.Request(await f.Options(b, 202), f.Start.AddHours(1)), clinic: 202);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode); Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        await f.Mutate(async db =>
        {
            var rows = await db.AppointmentNotificationOutbox.AsNoTracking().ToArrayAsync(); Assert.Equal(4, rows.Length);
            Assert.Equal(2, rows.Count(x => x.ClinicId == 101 && x.PlatformAccountId == 1));
            Assert.Equal(2, rows.Count(x => x.ClinicId == 202 && x.PlatformAccountId == 2));
            Assert.All(rows.Where(x => x.ClinicId == 202), x => Assert.Contains("Asia/Hovd", x.Body));
            var id = rows.First(x => x.ClinicId == 101).Id;
            var accountError = await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE AppointmentNotificationOutbox SET PlatformAccountId=2 WHERE Id={id}"));
            Assert.Equal(787, accountError.SqliteExtendedErrorCode);
            var clinicError = await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE AppointmentNotificationOutbox SET ClinicId=202 WHERE Id={id}"));
            Assert.Equal(787, clinicError.SqliteExtendedErrorCode);
            var duplicate = rows.First(x => x.ClinicId == 101);
            db.AppointmentNotificationOutbox.Add(new AppointmentNotification { AppointmentId=duplicate.AppointmentId,
                ClinicId=101, PlatformAccountId=1, Channel=duplicate.Channel, CreatedAtUtc=DateTime.UtcNow });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        });
        using var mine = await a.GetAsync("/api/platform/me/appointments?accountId=2");
        var json = await mine.Content.ReadAsStringAsync(); Assert.DoesNotContain("DEMO", json); Assert.DoesNotContain("Body", json);
        var appointments = (await mine.Content.ReadFromJsonAsync<AppointmentResponse[]>())!;
        Assert.Single(appointments); Assert.Equal(101, appointments[0].ClinicId);
        using var hidden = await b.GetAsync($"/api/platform/me/appointments/{appointments[0].Id}");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        using var noEndpoint = await a.GetAsync("/api/platform/me/notifications"); Assert.Equal(HttpStatusCode.NotFound, noEndpoint.StatusCode);
    }

    [Fact]
    public async Task Cleanup_erases_all_expired_content_but_retains_deduplication_and_booking()
    {
        using var f = new Fixture(); await f.Seed(); using var client = await f.Account(); var o = await f.Options(client);
        var key=Guid.NewGuid(); using var booked=await f.Book(client, f.Request(o), key);
        Assert.Equal(HttpStatusCode.Created, booked.StatusCode);
        await f.Mutate(async db =>
        {
            Assert.Equal(2, await Processor(db).ProcessBatchAsync());
            Assert.Equal(2, await Processor(db, new Clock(DateTimeOffset.UtcNow.AddHours(25))).CleanupAsync());
            Assert.Equal(0, await Processor(db).ProcessBatchAsync());
            db.ChangeTracker.Clear();
            Assert.All(await db.AppointmentNotificationOutbox.ToArrayAsync(), x => { Assert.Null(x.Body); Assert.Null(x.Subject); Assert.Equal("DemoGenerated", x.Status); });
            Assert.Equal(1, await db.Appointments.CountAsync());
        });
        using var replay=await f.Book(client, f.Request(o), key); Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        await f.Mutate(async db => { Assert.Equal(2, await db.AppointmentNotificationOutbox.CountAsync()); Assert.All(await db.AppointmentNotificationOutbox.ToArrayAsync(), x=>Assert.Null(x.Body)); });
    }

    [Fact]
    public async Task Concurrent_processors_claim_each_intent_once_and_recover_expired_lease()
    {
        using var f = new Fixture(); await f.Seed(); using var client = await f.Account();
        using var booked=await f.Book(client, f.Request(await f.Options(client))); Assert.Equal(HttpStatusCode.Created, booked.StatusCode);
        async Task<int> Run()
        {
            using var scope=f.App.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<AppointmentNotificationProcessor>().ProcessBatchAsync();
        }
        var results=await Task.WhenAll(Run(),Run()); Assert.Equal(2,results.Sum());
        await f.Mutate(async db =>
        {
            Assert.All(await db.AppointmentNotificationOutbox.AsNoTracking().ToArrayAsync(), x=>Assert.Equal(1,x.AttemptCount));
            await db.AppointmentNotificationOutbox.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,"Processing")
                .SetProperty(x=>x.LeaseToken,(Guid?)Guid.NewGuid()).SetProperty(x=>x.LeaseExpiresAtUtc,(DateTime?)DateTime.UtcNow.AddMinutes(-1)));
            Assert.Equal(2, await Processor(db).ProcessBatchAsync());
        });
    }
    [Fact]
    public async Task Concurrent_idempotent_booking_requests_record_exactly_two_intents()
    {
        using var f = new Fixture(); await f.Seed(); using var a=await f.Account(); using var b=await f.Account();
        var o=await f.Options(a); var key=Guid.NewGuid();
        var responses=await Task.WhenAll(f.Book(a,f.Request(o),key), f.Book(b,f.Request(o),key));
        try
        {
            Assert.Single(responses, x=>x.StatusCode==HttpStatusCode.Created);
            Assert.All(responses,x=>Assert.Contains(x.StatusCode,new[]{HttpStatusCode.Created,HttpStatusCode.OK,HttpStatusCode.Conflict}));
            using var replay=await f.Book(a,f.Request(o),key); Assert.Equal(HttpStatusCode.OK,replay.StatusCode);
            await f.Mutate(async db=> { Assert.Equal(1,await db.Appointments.CountAsync()); Assert.Equal(2,await db.AppointmentNotificationOutbox.CountAsync()); });
        }
        finally { foreach(var response in responses) response.Dispose(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Expired_pending_or_processing_content_is_erased_without_regeneration(bool processing)
    {
        using var f = new Fixture(); await f.Seed(); using var client=await f.Account();
        using var booked=await f.Book(client,f.Request(await f.Options(client))); Assert.Equal(HttpStatusCode.Created,booked.StatusCode);
        await f.Mutate(async db=>
        {
            if(processing) await db.AppointmentNotificationOutbox.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,"Processing")
                .SetProperty(x=>x.LeaseToken,(Guid?)Guid.NewGuid()).SetProperty(x=>x.LeaseExpiresAtUtc,(DateTime?)DateTime.UtcNow.AddMinutes(1)));
            Assert.Equal(0,await Processor(db,new Clock(DateTimeOffset.UtcNow.AddHours(25))).ProcessBatchAsync());
            Assert.All(await db.AppointmentNotificationOutbox.AsNoTracking().ToArrayAsync(),x=>
            { Assert.Null(x.Body); Assert.Null(x.Subject); Assert.Null(x.LeaseToken); Assert.Equal("Failed",x.Status); Assert.Equal("DEMO_CONTENT_EXPIRED",x.LastErrorCode); });
        });
    }

    [Fact]
    public async Task Crash_on_final_attempt_becomes_failed_after_lease_expires()
    {
        using var f = new Fixture(); await f.Seed(); using var client=await f.Account();
        using var booked=await f.Book(client,f.Request(await f.Options(client))); Assert.Equal(HttpStatusCode.Created,booked.StatusCode);
        await f.Mutate(async db=>
        {
            await db.AppointmentNotificationOutbox.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,"Processing")
                .SetProperty(x=>x.AttemptCount,3).SetProperty(x=>x.LeaseToken,(Guid?)Guid.NewGuid())
                .SetProperty(x=>x.LeaseExpiresAtUtc,(DateTime?)DateTime.UtcNow.AddMinutes(-1)));
            Assert.Equal(0,await Processor(db).ProcessBatchAsync());
            Assert.All(await db.AppointmentNotificationOutbox.AsNoTracking().ToArrayAsync(),x=>
            { Assert.Equal("Failed",x.Status); Assert.Null(x.LeaseToken); Assert.Equal("DEMO_ATTEMPTS_EXHAUSTED",x.LastErrorCode); });
        });
    }

}
