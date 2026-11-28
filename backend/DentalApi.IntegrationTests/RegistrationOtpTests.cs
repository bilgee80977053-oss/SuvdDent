using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using DentalApi.Data;
using DentalApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DentalApi.IntegrationTests;

public sealed class RegistrationOtpTests
{
    private const string Email = "registration@example.invalid";
    private const string Phone = "99901234";

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void AdvanceSeconds(int seconds) => now = now.AddSeconds(seconds);
    }

    private sealed class EmailSink : EmailService
    {
        public EmailSink() : base(new ConfigurationBuilder().Build()) { }

        public bool Available { get; init; } = true;
        public bool Fail { get; init; }
        public override bool IsAvailable => Available;
        public string? Code { get; private set; }
        public string? Destination { get; private set; }

        public override Task SendAsync(string toEmail, string subject, string body, string? messageId = null,
            CancellationToken cancellationToken = default)
        {
            Destination = toEmail;
            Code = Regex.Match(body, "Таны баталгаажуулах код: ([0-9]{6})").Groups[1].Value;
            Assert.Matches("^[0-9]{6}$", Code);
            if (Fail) throw new InvalidOperationException("synthetic adapter failure");
            return Task.CompletedTask;
        }
    }

    private sealed class SmsSink : IPhoneVerificationSmsSender
    {
        public bool IsAvailable { get; init; } = true;
        public string? Code { get; private set; }
        public string? Destination { get; private set; }

        public Task SendAsync(string destination, string code, string deliveryKey, CancellationToken ct)
        {
            Assert.Matches("^[0-9]{6}$", code);
            Destination = destination;
            Code = code;
            return Task.CompletedTask;
        }
    }

    private static DentalApiFactory Fixture(EmailSink email, SmsSink sms, ManualClock clock) =>
        new()
        {
            CustomizeServices = s =>
            {
                s.RemoveAll<EmailService>();
                s.RemoveAll<IPhoneVerificationSmsSender>();
                s.RemoveAll<TimeProvider>();
                s.AddSingleton<EmailService>(email);
                s.AddSingleton<IPhoneVerificationSmsSender>(sms);
                s.AddSingleton<TimeProvider>(clock);
            }
        };

    private static Task<HttpResponseMessage> Register(HttpClient client, string method,
        string password = DentalApiFactory.Password) =>
        client.PostAsJsonAsync("/api/Auth/register", new
        {
            fullName = "Synthetic registration",
            clinicId = 101,
            email = Email,
            phone = Phone,
            password,
            verificationMethod = method
        });

    private static Task<HttpResponseMessage> Request(HttpClient client, int id, string token, string method) =>
        client.PostAsJsonAsync("/api/Auth/registration-code",
            new { userId = id, verificationMethod = method, registrationToken = token });

    private static Task<HttpResponseMessage> Confirm(HttpClient client, int id, string token, string code) =>
        client.PostAsJsonAsync("/api/Auth/verify", new { userId = id, registrationToken = token, code });

    private static Task<HttpResponseMessage> Login(HttpClient client, string identifier) =>
        client.PostAsJsonAsync("/api/Auth/login", new { identifier, password = DentalApiFactory.Password, clinicId = 101 });

    private static async Task<(int Id, string Token)> Receipt(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var body = json.RootElement;
        Assert.Equal(60, body.GetProperty("retryAfterSeconds").GetInt32());
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.False(body.TryGetProperty("code", out _));
        return (body.GetProperty("userId").GetInt32(), body.GetProperty("registrationToken").GetString()!);
    }

    private static async Task AssertReason(HttpResponseMessage response, HttpStatusCode status, string reason)
    {
        Assert.Equal(status, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(reason, json.RootElement.GetProperty("reason").GetString());
    }

    [Theory]
    [InlineData("email")]
    [InlineData("sms")]
    public async Task Selected_only_channel_activates_patient_and_allows_only_verified_login(string method)
    {
        var email = new EmailSink();
        var sms = new SmsSink();
        using var app = Fixture(email, sms, new ManualClock());
        using var client = app.CreateTestClient();
        await app.SeedAsync();
        var otherClinic = await app.SnapshotBAsync();
        using var started = await Register(client, method);
        var (id, token) = await Receipt(started, HttpStatusCode.OK);
        Assert.Equal(method == "email" ? Email : null, email.Destination);
        Assert.Equal(method == "sms" ? "+976" + Phone : null, sms.Destination);
        var unchosenContact = method == "email" ? Phone : Email;
        var chosenContact = method == "email" ? Email : Phone;
        using var verified = await Confirm(client, id, token, (email.Code ?? sms.Code)!);
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        using var login = await Login(client, chosenContact);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var unverifiedLogin = await Login(client, unchosenContact);
        Assert.Equal(HttpStatusCode.Unauthorized, unverifiedLogin.StatusCode);
        using var replay = await Request(client, id, token, "sms");
        await AssertReason(replay, HttpStatusCode.BadRequest, "invalid_registration");
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var patient = await db.Users.SingleAsync(x => x.Id == id);
        Assert.True(patient.IsActive);
        Assert.Equal(method == "email", patient.IsEmailVerified);
        Assert.Equal(method == "sms", patient.IsPhoneVerified);
        Assert.Equal(otherClinic, await app.SnapshotBAsync());
    }

    [Fact]
    public async Task Switching_shares_cooldown_and_invalidates_old_code_without_touching_another_registration()
    {
        var email = new EmailSink();
        var sms = new SmsSink();
        var clock = new ManualClock();
        using var app = Fixture(email, sms, clock);
        using var client = app.CreateTestClient();
        await app.SeedAsync();
        using var started = await Register(client, "email");
        var (id, token) = await Receipt(started, HttpStatusCode.OK);

        using var early = await Request(client, id, token, "sms");
        await AssertReason(early, HttpStatusCode.TooManyRequests, "retry_cooldown");
        Assert.Equal(TimeSpan.FromSeconds(60), early.Headers.RetryAfter!.Delta!.Value);
        Assert.Null(sms.Code);
        clock.AdvanceSeconds(60);
        using var foreign = await Request(client, 12, token, "sms");
        await AssertReason(foreign, HttpStatusCode.BadRequest, "invalid_registration");

        using var changed = await Request(client, id, token, "sms");
        await Receipt(changed, HttpStatusCode.OK);
        // Inspect explicit invalidation rather than relying on two random OTPs never colliding.
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var codes = await db.VerificationCodes.Where(x => x.UserId == id).OrderBy(x => x.Id).ToListAsync();
            Assert.Equal(2, codes.Count);
            Assert.Equal("email", codes[0].Channel);
            Assert.True(codes[0].IsUsed);
            Assert.Equal("sms", codes[1].Channel);
            Assert.False(codes[1].IsUsed);
        }
        using var verified = await Confirm(client, id, token, sms.Code!);
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
    }

    [Fact]
    public async Task Bad_expired_and_legacy_unbound_verification_cannot_activate_choice_account()
    {
        var email = new EmailSink();
        var clock = new ManualClock();
        using var app = Fixture(email, new SmsSink(), clock);
        using var client = app.CreateTestClient();
        await app.SeedAsync();
        using var started = await Register(client, "email");
        var (id, token) = await Receipt(started, HttpStatusCode.OK);
        using var legacy = await client.PostAsJsonAsync("/api/Auth/verify", new { userId = id, code = email.Code });
        Assert.Equal(HttpStatusCode.BadRequest, legacy.StatusCode);
        using var malformed = await Confirm(client, id, "!?", email.Code!);
        await AssertReason(malformed, HttpStatusCode.BadRequest, "invalid_registration");
        using var wrong = await Confirm(client, id, token, email.Code == "000000" ? "000001" : "000000");
        await AssertReason(wrong, HttpStatusCode.BadRequest, "invalid_code");
        clock.AdvanceSeconds(120);
        using var expired = await Confirm(client, id, token, email.Code!);
        await AssertReason(expired, HttpStatusCode.BadRequest, "expired_code");
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False((await db.Users.SingleAsync(x => x.Id == id)).IsActive);
        Assert.Equal(1, (await db.VerificationCodes.SingleAsync(x => x.UserId == id)).FailedAttempts);
    }

    [Fact]
    public async Task Failed_initial_delivery_cannot_verify_but_token_allows_recovery_by_other_channel()
    {
        var email = new EmailSink { Fail = true };
        var sms = new SmsSink();
        var clock = new ManualClock();
        using var app = Fixture(email, sms, clock);
        using var client = app.CreateTestClient();
        await app.SeedAsync();
        using var failed = await Register(client, "email");
        var (id, token) = await Receipt(failed, HttpStatusCode.ServiceUnavailable);
        using var unaccepted = await Confirm(client, id, token, email.Code!);
        await AssertReason(unaccepted, HttpStatusCode.BadRequest, "invalid_code");
        clock.AdvanceSeconds(60);
        using var changed = await Request(client, id, token, "sms");
        await Receipt(changed, HttpStatusCode.OK);
        using var verified = await Confirm(client, id, token, sms.Code!);
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
    }

    [Fact]
    public async Task Failed_switch_keeps_previous_code_confirmable()
    {
        var email = new EmailSink { Fail = true };
        var sms = new SmsSink();
        var clock = new ManualClock();
        using var app = Fixture(email, sms, clock);
        using var client = app.CreateTestClient();
        await app.SeedAsync();
        using var initial = await Register(client, "sms");
        var (id, token) = await Receipt(initial, HttpStatusCode.OK);
        clock.AdvanceSeconds(60);
        using var failedSwitch = await Request(client, id, token, "email");
        await Receipt(failedSwitch, HttpStatusCode.ServiceUnavailable);
        using var stillValid = await Confirm(client, id, token, sms.Code!);
        Assert.Equal(HttpStatusCode.OK, stillValid.StatusCode);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True((await db.Users.SingleAsync(x => x.Id == id)).IsPhoneVerified);
    }

    [Fact]
    public async Task Attempt_budget_is_kept_across_channels()
    {
        var email = new EmailSink();
        var sms = new SmsSink();
        var clock = new ManualClock();
        using var app = Fixture(email, sms, clock);
        using var client = app.CreateTestClient();
        await app.SeedAsync();
        using var initial = await Register(client, "sms");
        var (id, token) = await Receipt(initial, HttpStatusCode.OK);
        // Simulate four earlier failures already persisted, without exhausting the test client's
        // independent production IP budget before the per-registration limit can be tested.
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.VerificationCodes.Where(x => x.UserId == id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.FailedAttempts, 4));
        }
        clock.AdvanceSeconds(60);
        using var switched = await Request(client, id, token, "email");
        await Receipt(switched, HttpStatusCode.OK);
        using var finalFailure = await Confirm(client, id, token, email.Code == "000000" ? "000001" : "000000");
        await AssertReason(finalFailure, HttpStatusCode.BadRequest, "invalid_code");
        using var blocked = await Confirm(client, id, token, email.Code!);
        await AssertReason(blocked, HttpStatusCode.TooManyRequests, "otp_limit");
    }

    [Fact]
    public async Task Original_credentials_can_resume_pending_registration_without_duplicate_send()
    {
        var email = new EmailSink();
        var sms = new SmsSink();
        var clock = new ManualClock();
        using var app = Fixture(email, sms, clock);
        using var client = app.CreateTestClient();
        await app.SeedAsync();
        using var initial = await Register(client, "email");
        var (id, _) = await Receipt(initial, HttpStatusCode.OK);
        using var wrongPassword = await Register(client, "sms", "wrong synthetic password");
        Assert.Equal(HttpStatusCode.Conflict, wrongPassword.StatusCode);
        using var earlyRecovery = await Register(client, "sms");
        await AssertReason(earlyRecovery, HttpStatusCode.TooManyRequests, "retry_cooldown");
        using var recoveryJson = JsonDocument.Parse(await earlyRecovery.Content.ReadAsStringAsync());
        Assert.Equal(id, recoveryJson.RootElement.GetProperty("userId").GetInt32());
        Assert.Equal("email", recoveryJson.RootElement.GetProperty("verificationMethod").GetString());
        Assert.Null(sms.Code);
        clock.AdvanceSeconds(60);
        using var recovered = await Register(client, "sms");
        var (recoveredId, recoveredToken) = await Receipt(recovered, HttpStatusCode.OK);
        Assert.Equal(id, recoveredId);
        using var verified = await Confirm(client, recoveredId, recoveredToken, sms.Code!);
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
    }

    [Fact]
    public async Task Unavailable_channel_does_not_reserve_account_or_send()
    {
        var email = new EmailSink { Available = false };
        using var app = Fixture(email, new SmsSink(), new ManualClock());
        using var client = app.CreateTestClient();
        await app.SeedAsync();
        using var failed = await Register(client, "email");
        await AssertReason(failed, HttpStatusCode.ServiceUnavailable, "delivery_unavailable");
        Assert.Null(email.Code);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Users.AnyAsync(x => x.Email == Email));
    }
}
