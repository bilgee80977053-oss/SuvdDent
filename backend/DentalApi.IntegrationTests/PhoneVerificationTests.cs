using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using DentalApi.Contracts;
using DentalApi.Data;
using DentalApi.Models;
using DentalApi.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DentalApi.IntegrationTests;

public sealed class PhoneVerificationTests
{
    private const string Url = "/api/phone-verification/me";
    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class CommitFailure : DbTransactionInterceptor
    {
        internal bool FailNext;
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (FailNext) { FailNext = false; throw new SqliteException("synthetic-commit-failure", 5); }
            return ValueTask.FromResult(result);
        }
    }
    private sealed class CapturedLogs : ILoggerProvider
    {
        internal readonly ConcurrentQueue<string> Lines = new();
        private sealed class CapturedLogger(ConcurrentQueue<string> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel level) => true;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error,
                Func<TState, Exception?, string> formatter) => lines.Enqueue(formatter(state, error));
        }
        public ILogger CreateLogger(string categoryName) => new CapturedLogger(Lines);
        public void Dispose() { }
    }
    private sealed class ControlledSms : ISmsSender
    {
        internal readonly FakeSmsSender Recorder = new();
        internal TaskCompletionSource? Entered;
        internal TaskCompletionSource? Continue;
        internal Exception? Error;
        internal Action? AfterSend;
        internal string? LastBody;
        public async Task SendAsync(string recipient, string body, string key, CancellationToken ct)
        {
            LastBody = body;
            Entered?.TrySetResult();
            if (Continue is not null) await Continue.Task.WaitAsync(ct);
            if (Error is not null) throw Error;
            await Recorder.SendAsync(recipient, body, key, ct);
            AfterSend?.Invoke();
        }
    }
    private sealed class Factory : DoctorApiFactory
    {
        internal bool Enabled = true;
        internal string Endpoint = "https://sms.example.invalid/verification";
        internal readonly Clock Time = new();
        internal readonly ControlledSms Sms = new();
        internal readonly CommitFailure Failure = new();
        internal readonly CapturedLogs Logs = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Notifications:Sms:Enabled"] = Enabled.ToString(), ["Notifications:Sms:Endpoint"] = Endpoint
            }));
            builder.ConfigureLogging(logging => logging.AddProvider(Logs));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ISmsSender>(); services.AddSingleton<ISmsSender>(Sms);
                services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(Time);
                services.AddDbContext<AppDbContext>(options => options.AddInterceptors(Failure));
            });
        }
    }
    private static async Task<Factory> App(bool enabled = true, string endpoint = "https://sms.example.invalid/verification")
    {
        var app = new Factory { Enabled = enabled, Endpoint = endpoint };
        await app.SeedAsync();
        return app;
    }
    private static async Task Mutate(Factory app, Func<AppDbContext, Task> action)
    {
        using var scope = app.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }
    private static async Task<PhoneVerificationChallenge[]> Rows(Factory app)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .Set<PhoneVerificationChallenge>().AsNoTracking().OrderBy(x => x.CreatedUtcTicks).ToArrayAsync();
    }
    private static string Code(Factory app) =>
        Regex.Match(app.Sms.LastBody!, @"(?<![0-9])[0-9]{6}(?![0-9])").Value;
    private static string Wrong(string code) => code == "000000" ? "000001" : "000000";
    private static async Task<PhoneVerificationRequestedResponse> Request(Factory app, HttpClient client)
    {
        using var response = await client.PostAsync(Url + "/request", null);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(!body.Contains(Code(app), StringComparison.Ordinal), "OTP must not appear in responses.");
        return (await response.Content.ReadFromJsonAsync<PhoneVerificationRequestedResponse>())!;
    }
    private static async Task Confirm(HttpClient client, Guid id, string code, HttpStatusCode expected)
    {
        using var response = await client.PostAsJsonAsync(Url + "/confirm", new { challengeId = id, code });
        Assert.Equal(expected, response.StatusCode);
        Assert.True(!(await response.Content.ReadAsStringAsync()).Contains(code, StringComparison.Ordinal),
            "OTP must not appear in responses.");
    }
    private static async Task Verified(Factory app, bool expected)
    {
        await Mutate(app, async db => Assert.Equal(expected,
            (await db.Users.AsNoTracking().SingleAsync(x => x.Id == 11)).IsPhoneVerified));
    }

    [Theory]
    [InlineData(0, 401)] [InlineData(13, 403)] [InlineData(15, 403)] [InlineData(16, 403)]
    public async Task Every_endpoint_requires_authenticated_patient(int user, int expected)
    {
        using var app = await App();
        using var client = user == 0 ? app.Client() : await app.AsAsync(user);
        using var status = await client.GetAsync(Url);
        using var request = await client.PostAsync(Url + "/request", null);
        using var confirm = await client.PostAsJsonAsync(Url + "/confirm", new { challengeId = Guid.NewGuid(), code = "000000" });
        Assert.Equal((HttpStatusCode)expected, status.StatusCode);
        Assert.Equal((HttpStatusCode)expected, request.StatusCode);
        Assert.Equal((HttpStatusCode)expected, confirm.StatusCode);
        Assert.Empty(await Rows(app)); Assert.Empty(app.Sms.Recorder.Sent);
    }

    [Fact]
    public async Task Correct_code_verifies_only_existing_account_phone_and_preserves_auth_profile_and_consent()
    {
        using var app = await App(); using var client = await app.AsAsync(11);
        var before = await client.GetFromJsonAsync<PhoneVerificationStatusResponse>(Url);
        Assert.Equal("+976****0011", before!.MaskedPhone);
        Assert.False(before.IsPhoneVerified); Assert.True(before.DeliveryAvailable);
        string? password = null;
        await Mutate(app, async db => password = (await db.Users.SingleAsync(x => x.Id == 11)).PasswordHash);
        var request = await Request(app, client);
        var code = Code(app);
        Assert.Equal(app.Time.Now.AddSeconds(120), request.ExpiresAtUtc);
        Assert.Equal(app.Time.Now.AddSeconds(60), request.ResendAtUtc);
        var row = Assert.Single(await Rows(app));
        Assert.Equal(PhoneVerificationDeliveryState.Accepted, row.DeliveryState);
        Assert.True(BCrypt.Net.BCrypt.Verify(code, row.CodeHash), "Only the hashed OTP is stored.");
        Assert.False(row.IsConsumed);
        await Verified(app, false);
        var sent = Assert.Single(app.Sms.Recorder.Sent);
        Assert.Equal("+97600000011", sent.Recipient);
        Assert.Equal(row.Id.ToString("N"), sent.Key);
        await Confirm(client, request.ChallengeId, code, HttpStatusCode.OK);
        await Verified(app, true);
        Assert.True(Assert.Single(await Rows(app)).IsConsumed);
        await Mutate(app, async db =>
        {
            var user = await db.Users.SingleAsync(x => x.Id == 11);
            Assert.Equal("00000011", user.Phone);
            Assert.Equal(password, user.PasswordHash); Assert.Equal(0, user.TokenVersion);
            Assert.True(user.IsEmailVerified); Assert.True(user.IsActive);
            Assert.Equal("11111111", (await db.Patients.SingleAsync(x => x.UserId == 11)).Phone);
            Assert.Empty(await db.Set<NotificationPreference>().ToArrayAsync());
            Assert.Empty(await db.Set<NotificationOutbox>().ToArrayAsync());
            Assert.Empty(await db.VerificationCodes.ToArrayAsync());
        });
        using var login = await client.PostAsJsonAsync("/api/Auth/login",
            new { identifier = "00000011", password = DentalApiFactory.Password, clinicId = 101 });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var repeatedRequest = await client.PostAsync(Url + "/request", null);
        Assert.Equal(HttpStatusCode.Conflict, repeatedRequest.StatusCode);
    }

    [Theory]
    [InlineData("88112233", "+97688112233")]
    [InlineData("+97688112233", "+97688112233")]
    [InlineData("97688112233", "+97688112233")]
    public async Task Normalization_is_transport_only_and_never_rewrites_login_phone(string phone, string destination)
    {
        using var app = await App(); using var client = await app.AsAsync(11);
        await Mutate(app, async db =>
        {
            (await db.Users.SingleAsync(x => x.Id == 11)).Phone = phone; await db.SaveChangesAsync();
        });
        var request = await Request(app, client);
        Assert.Equal(destination, Assert.Single(app.Sms.Recorder.Sent).Recipient);
        await Confirm(client, request.ChallengeId, Code(app), HttpStatusCode.OK);
        await Mutate(app, async db => Assert.Equal(phone, (await db.Users.SingleAsync(x => x.Id == 11)).Phone));
    }

    [Theory]
    [InlineData(null)] [InlineData("invalid")] [InlineData("1234567")] [InlineData("+12345678901")]
    public async Task Unsupported_account_phone_cannot_generate_a_challenge(string? phone)
    {
        using var app = await App(); using var client = await app.AsAsync(11);
        await Mutate(app, async db => { (await db.Users.SingleAsync(x => x.Id == 11)).Phone = phone; await db.SaveChangesAsync(); });
        var status = await client.GetFromJsonAsync<PhoneVerificationStatusResponse>(Url);
        Assert.Null(status!.MaskedPhone); Assert.False(status.PhoneEligible);
        using var response = await client.PostAsync(Url + "/request", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await Rows(app)); Assert.Empty(app.Sms.Recorder.Sent);
    }

    [Fact]
    public async Task Wrong_expired_and_reused_codes_never_verify()
    {
        using var app = await App(); using var client = await app.AsAsync(11);
        var first = await Request(app, client);
        var code = Code(app);
        await Confirm(client, first.ChallengeId, Wrong(code), HttpStatusCode.BadRequest);
        Assert.Equal(1, Assert.Single(await Rows(app)).FailedAttempts);
        await Verified(app, false);
        app.Time.Now = first.ExpiresAtUtc;
        await Confirm(client, first.ChallengeId, code, HttpStatusCode.BadRequest);
        await Verified(app, false);
        var second = await Request(app, client);
        await Confirm(client, second.ChallengeId, Code(app), HttpStatusCode.OK);
        await Confirm(client, second.ChallengeId, Code(app), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Per_challenge_attempt_limit_and_rolling_failure_budget_are_persistent()
    {
        using var app = await App(); using var client = await app.AsAsync(11);
        var request = await Request(app, client);
        var code = Code(app);
        for (var i = 0; i < 5; i++)
            await Confirm(client, request.ChallengeId, Wrong(code), HttpStatusCode.BadRequest);
        var row = Assert.Single(await Rows(app));
        Assert.Equal(5, row.FailedAttempts); Assert.True(row.IsInvalidated);
        await Confirm(client, request.ChallengeId, code, HttpStatusCode.BadRequest);
        app.Time.Now = app.Time.Now.AddSeconds(60);
        using var blocked = await client.PostAsync(Url + "/request", null);
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        app.Time.Now = app.Time.Now.AddMinutes(15);
        await Request(app, client);
        await Verified(app, false);
    }

    [Fact]
    public async Task Resend_invalidates_old_code_without_resetting_aggregate_failures()
    {
        using var app = await App(); using var client = await app.AsAsync(11);
        var first = await Request(app, client);
        var oldCode = Code(app);
        for (var i = 0; i < 3; i++) await Confirm(client, first.ChallengeId, Wrong(oldCode), HttpStatusCode.BadRequest);
        app.Time.Now = app.Time.Now.AddSeconds(60);
        var second = await Request(app, client);
        var code = Code(app);
        await Confirm(client, first.ChallengeId, oldCode, HttpStatusCode.BadRequest);
        for (var i = 0; i < 2; i++) await Confirm(client, second.ChallengeId, Wrong(code), HttpStatusCode.BadRequest);
        await Confirm(client, second.ChallengeId, code, HttpStatusCode.TooManyRequests);
        app.Time.Now = app.Time.Now.AddSeconds(60);
        using var blocked = await client.PostAsync(Url + "/request", null);
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.Equal(5, (await Rows(app)).Sum(x => x.FailedAttempts));
        await Verified(app, false);
    }

    [Fact]
    public async Task Cooldown_and_daily_limit_include_all_reservations_and_expire_at_boundary()
    {
        using var app = await App(); using var client = await app.AsAsync(11);
        await Request(app, client);
        app.Time.Now = app.Time.Now.AddSeconds(59);
        using (var early = await client.PostAsync(Url + "/request", null))
            Assert.Equal(HttpStatusCode.TooManyRequests, early.StatusCode);
        app.Time.Now = app.Time.Now.AddSeconds(1);
        for (var i = 1; i < 5; i++)
        {
            await Request(app, client);
            app.Time.Now = app.Time.Now.AddSeconds(60);
        }
        using (var blocked = await client.PostAsync(Url + "/request", null))
            Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.Equal(5, (await Rows(app)).Length); Assert.Equal(5, app.Sms.Recorder.Sent.Count);
        app.Time.Now = app.Time.Now.AddHours(24);
        await Request(app, client);
    }

    [Fact]
    public async Task Challenges_are_scoped_to_user_and_clinic_and_input_cannot_replace_phone()
    {
        using var app = await App(); using var a = await app.AsAsync(11); using var b = await app.AsAsync(12, 202);
        await Mutate(app, async db =>
        {
            db.Users.Add(new User { Id = 27, ClinicId = 101, Role = Role.Patient, IsActive = true,
                Email = "user27@example.invalid", PasswordHash = BCrypt.Net.BCrypt.HashPassword(DentalApiFactory.Password, 4) });
            await db.SaveChangesAsync();
        });
        using var other = await app.AsAsync(27);
        var request = await Request(app, a);
        var code = Code(app);
        await Confirm(b, request.ChallengeId, code, HttpStatusCode.BadRequest);
        await Confirm(other, request.ChallengeId, code, HttpStatusCode.BadRequest);
        using var replacement = await a.PostAsJsonAsync(Url + "/request", new { phone = "99999999" });
        Assert.Equal(HttpStatusCode.BadRequest, replacement.StatusCode);
        using var spoof = await a.PostAsJsonAsync(Url + "/confirm",
            new { challengeId = request.ChallengeId, code, userId = 12, clinicId = 202, phone = "99999999" });
        Assert.Equal(HttpStatusCode.BadRequest, spoof.StatusCode);
        using var query = await a.PostAsJsonAsync(Url + "/confirm?userId=12&clinicId=202",
            new { challengeId = request.ChallengeId, code });
        Assert.Equal(HttpStatusCode.OK, query.StatusCode);
        await Mutate(app, async db => Assert.False((await db.Users.SingleAsync(x => x.Id == 12)).IsPhoneVerified));
    }

    [Theory]
    [InlineData(false, "https://sms.example.invalid/verification")]
    [InlineData(true, "http://sms.example.invalid/verification")]
    [InlineData(true, "invalid")]
    public async Task Disabled_or_unconfigured_provider_is_503_without_creating_challenge(bool enabled, string endpoint)
    {
        using var app = await App(enabled, endpoint); using var client = await app.AsAsync(11);
        var status = await client.GetFromJsonAsync<PhoneVerificationStatusResponse>(Url);
        Assert.False(status!.DeliveryAvailable);
        using var request = await client.PostAsync(Url + "/request", null);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, request.StatusCode);
        Assert.Empty(await Rows(app)); Assert.Empty(app.Sms.Recorder.Sent);
        await Verified(app, false);
    }

    [Theory]
    [InlineData("failure")] [InlineData("timeout")]
    public async Task Transport_errors_never_leave_a_confirmable_challenge(string failure)
    {
        using var app = await App(); using var client = await app.AsAsync(11);
        app.Sms.Error = failure == "timeout" ? new OperationCanceledException("synthetic-timeout") :
            new InvalidOperationException("synthetic-provider-failure");
        using var request = await client.PostAsync(Url + "/request", null);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, request.StatusCode);
        var row = Assert.Single(await Rows(app));
        Assert.True(row.IsInvalidated); Assert.Equal(PhoneVerificationDeliveryState.Failed, row.DeliveryState);
        await Confirm(client, row.Id, Code(app), HttpStatusCode.BadRequest);
        await Verified(app, false);
        Assert.DoesNotContain(app.Logs.Lines, line => line.Contains("synthetic-provider-failure"));
    }

    [Fact]
    public async Task Pending_delivery_cannot_confirm_and_changed_snapshot_invalidates_accepted_code()
    {
        using var app = await App(); using var client = await app.AsAsync(11);
        app.Sms.Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        app.Sms.Continue = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var sending = client.PostAsync(Url + "/request", null);
        await app.Sms.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var row = Assert.Single(await Rows(app));
        Assert.Equal(PhoneVerificationDeliveryState.Pending, row.DeliveryState);
        await Confirm(client, row.Id, Code(app), HttpStatusCode.BadRequest);
        app.Sms.Continue.SetResult();
        using var sent = await sending;
        Assert.Equal(HttpStatusCode.Accepted, sent.StatusCode);
        await Mutate(app, async db =>
        {
            (await db.Users.SingleAsync(x => x.Id == 11)).Phone = "99999999"; await db.SaveChangesAsync();
        });
        await Confirm(client, row.Id, Code(app), HttpStatusCode.BadRequest);
        Assert.True(Assert.Single(await Rows(app)).IsInvalidated);
        await Verified(app, false);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Concurrent_initial_requests_or_resends_reserve_and_send_only_once(bool resend)
    {
        using var app = await App(); using var a = await app.AsAsync(11); using var b = await app.AsAsync(11);
        if (resend) { await Request(app, a); app.Time.Now = app.Time.Now.AddSeconds(60); }
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<HttpResponseMessage> Send(HttpClient client) { await gate.Task; return await client.PostAsync(Url + "/request", null); }
        var first = Task.Run(() => Send(a)); var second = Task.Run(() => Send(b)); gate.SetResult();
        var results = await Task.WhenAll(first, second);
        try
        {
            Assert.Equal(1, results.Count(x => x.StatusCode == HttpStatusCode.Accepted));
            Assert.All(results, x => Assert.Contains(x.StatusCode,
                new[] { HttpStatusCode.Accepted, HttpStatusCode.Conflict, HttpStatusCode.TooManyRequests }));
            Assert.Equal(resend ? 2 : 1, app.Sms.Recorder.Sent.Count);
            Assert.Equal(resend ? 2 : 1, (await Rows(app)).Length);
            Assert.Single(await Rows(app), x => !x.IsConsumed && !x.IsInvalidated);
        }
        finally { foreach (var response in results) response.Dispose(); }
    }

    [Fact]
    public async Task Concurrent_confirmations_consume_exactly_once()
    {
        using var app = await App(); using var a = await app.AsAsync(11); using var b = await app.AsAsync(11);
        var request = await Request(app, a); var code = Code(app);
        var results = await Task.WhenAll(
            Task.Run(() => a.PostAsJsonAsync(Url + "/confirm", new { challengeId = request.ChallengeId, code })),
            Task.Run(() => b.PostAsJsonAsync(Url + "/confirm", new { challengeId = request.ChallengeId, code })));
        try
        {
            Assert.Equal(1, results.Count(x => x.StatusCode == HttpStatusCode.OK));
            Assert.All(results, x => Assert.Contains(x.StatusCode,
                new[] { HttpStatusCode.OK, HttpStatusCode.BadRequest, HttpStatusCode.Conflict }));
            Assert.True(Assert.Single(await Rows(app)).IsConsumed);
        }
        finally { foreach (var response in results) response.Dispose(); }
    }

    [Fact]
    public async Task Reservation_acceptance_and_confirmation_commit_failures_are_safe()
    {
        using var app = await App(); using var client = await app.AsAsync(11);
        app.Failure.FailNext = true;
        using (var failed = await client.PostAsync(Url + "/request", null))
            Assert.Equal(HttpStatusCode.Conflict, failed.StatusCode);
        Assert.Empty(await Rows(app)); Assert.Empty(app.Sms.Recorder.Sent);

        app.Sms.AfterSend = () => app.Failure.FailNext = true;
        using (var failed = await client.PostAsync(Url + "/request", null))
            Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        var invalid = Assert.Single(await Rows(app));
        Assert.True(invalid.IsInvalidated); Assert.NotEqual(PhoneVerificationDeliveryState.Accepted, invalid.DeliveryState);
        await Confirm(client, invalid.Id, Code(app), HttpStatusCode.BadRequest);
        await Verified(app, false);

        app.Sms.AfterSend = null; app.Time.Now = app.Time.Now.AddSeconds(60);
        var request = await Request(app, client); var code = Code(app);
        app.Failure.FailNext = true;
        await Confirm(client, request.ChallengeId, code, HttpStatusCode.Conflict);
        await Verified(app, false);
        Assert.False(Assert.Single(await Rows(app), x => x.Id == request.ChallengeId).IsConsumed);
        await Confirm(client, request.ChallengeId, code, HttpStatusCode.OK);
    }

    [Fact]
    public async Task Responses_logs_and_persistence_do_not_expose_OTP_and_consent_stays_independent()
    {
        using var app = await App(); using var client = await app.AsAsync(11);
        using (var preference = await client.PutAsJsonAsync("/api/notification-preferences/me",
            new { emailEnabled = false, smsEnabled = true }))
            Assert.Equal(HttpStatusCode.OK, preference.StatusCode);
        await Verified(app, false);
        var request = await Request(app, client); var code = Code(app);
        using var status = await client.GetAsync(Url);
        Assert.True(!(await status.Content.ReadAsStringAsync()).Contains(code, StringComparison.Ordinal), "OTP leak.");
        Assert.DoesNotContain(app.Logs.Lines, x => x.Contains("код: " + code, StringComparison.Ordinal));
        var properties = typeof(PhoneVerificationChallenge).GetProperties().Select(x => x.Name).ToArray();
        Assert.DoesNotContain("Code", properties); Assert.DoesNotContain("Body", properties);
        await Confirm(client, request.ChallengeId, code, HttpStatusCode.OK);
        var prefs = await client.GetFromJsonAsync<NotificationPreferencesResponse>("/api/notification-preferences/me");
        Assert.False(prefs!.EmailEnabled); Assert.True(prefs.SmsEnabled); Assert.True(prefs.IsPhoneVerified);
        await Mutate(app, async db => Assert.Empty(await db.Set<NotificationOutbox>().ToArrayAsync()));
    }

    [Fact]
    public async Task Account_throttle_is_separate_from_other_accounts()
    {
        using var app = await App(); using var a = await app.AsAsync(11); using var b = await app.AsAsync(12, 202);
        for (var i = 0; i < 20; i++)
        {
            using var response = await a.GetAsync(Url);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        using var limited = await a.GetAsync(Url);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter);
        using var other = await b.GetAsync(Url);
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
    }

    [Fact]
    public async Task IP_throttle_spans_accounts()
    {
        using var app = await App();
        await Mutate(app, async db =>
        {
            foreach (var id in new[] { 27, 28 })
                db.Users.Add(new User { Id = id, ClinicId = 101, Role = Role.Patient, IsActive = true,
                    Email = $"user{id}@example.invalid", PasswordHash = BCrypt.Net.BCrypt.HashPassword(DentalApiFactory.Password, 4) });
            await db.SaveChangesAsync();
        });
        foreach (var id in new[] { 11, 12, 27 })
        {
            using var client = await app.AsAsync(id, id == 12 ? 202 : 101);
            for (var i = 0; i < 20; i++)
            {
                using var response = await client.GetAsync(Url);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
        }
        using var fourth = await app.AsAsync(28);
        using var limited = await fourth.GetAsync(Url);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }
}
