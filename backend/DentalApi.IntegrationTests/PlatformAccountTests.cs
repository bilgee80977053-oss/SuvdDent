using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DentalApi.Contracts;
using DentalApi.Data;
using DentalApi.Models;
using DentalApi.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DentalApi.IntegrationTests;

public sealed class PlatformAccountTests
{
    private const string Root = "/api/platform/auth/";
    private const string Password = "Synthetic-platform-pass-01!";
    private const string Changed = "Synthetic-platform-pass-02!";
    private static async Task Register(PlatformApiFactory app, HttpClient client, string email = "a@example.invalid", string? phone = null)
    {
        using var response = await client.PostAsJsonAsync(Root + "register", new PlatformRegisterRequest("Synthetic account", email, phone, Password));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }
    private static async Task Verify(PlatformApiFactory app, HttpClient client, string identifier = "a@example.invalid", string? deliveredTo = null)
    {
        using var response = await client.PostAsJsonAsync(Root + "verify", new PlatformVerifyRequest(identifier, app.Messages.Code(deliveredTo ?? identifier)));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
    private static async Task<PlatformLoginResponse> Login(HttpClient client, string identifier = "a@example.invalid", string password = Password)
    {
        using var response = await client.PostAsJsonAsync(Root + "login", new PlatformLoginRequest(identifier, password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PlatformLoginResponse>())!;
    }
    private static HttpClient Authorized(PlatformApiFactory app, string token)
    {
        var client = app.Client(); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token); return client;
    }

    [Fact]
    public async Task Registration_requires_verification_normalizes_contacts_and_creates_no_local_links()
    {
        using var app = new PlatformApiFactory(); await app.SeedAsync();
        using var client = app.Client();
        await Register(app, client, " A@EXAMPLE.INVALID ", "99112233");
        using var inactive = await client.PostAsJsonAsync(Root + "login", new PlatformLoginRequest("a@example.invalid", Password));
        Assert.Equal(HttpStatusCode.Unauthorized, inactive.StatusCode);
        await Verify(app, client);
        var login = await Login(client, "A@EXAMPLE.INVALID");
        using var authorized = Authorized(app, login.Token);
        using var response = await authorized.GetAsync(Root + "me?clinicId=202&accountId=999");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("passwordHash", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tokenVersion", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("clinicId", json, StringComparison.OrdinalIgnoreCase);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account = await db.PlatformAccounts.SingleAsync();
        Assert.Equal("a@example.invalid", account.Email); Assert.Equal("+97699112233", account.Phone);
        Assert.True(BCrypt.Net.BCrypt.Verify(Password, account.PasswordHash));
        Assert.NotEqual(Password, account.PasswordHash);
        Assert.True(account.IsEmailVerified); Assert.False(account.IsPhoneVerified);
        Assert.Empty(await db.ClinicPatientLinks.ToArrayAsync());
        Assert.Equal(2, await db.Patients.CountAsync());
        using var phone = await client.PostAsJsonAsync(Root + "login", new PlatformLoginRequest("99112233", Password));
        Assert.Equal(HttpStatusCode.Unauthorized, phone.StatusCode);
    }

    [Fact]
    public async Task Phone_only_accounts_use_fake_sms_and_verified_normalized_phone()
    {
        using var app = new PlatformApiFactory(); await app.SeedAsync(); using var client = app.Client();
        using var register = await client.PostAsJsonAsync(Root + "register", new PlatformRegisterRequest("Synthetic", null, "99112233", Password));
        Assert.Equal(HttpStatusCode.Accepted, register.StatusCode);
        Assert.Equal("sms", app.Messages.Messages.Single().Channel);
        await Verify(app, client, "99112233", "+97699112233");
        var login = await Login(client, "+97699112233");
        Assert.True(login.Account.IsPhoneVerified); Assert.Null(login.Account.Email);
    }

    [Theory]
    [InlineData("A@EXAMPLE.INVALID", null)]
    [InlineData(null, "+97699112233")]
    public async Task Duplicate_contacts_are_global_across_clinics(string? email, string? phone)
    {
        using var app = new PlatformApiFactory(); await app.SeedAsync(); using var client = app.Client();
        await Register(app, client, "a@example.invalid", "99112233");
        using var duplicate = await client.PostAsJsonAsync(Root + "register", new
        { fullName = "Synthetic other", email, phone, password = Password, clinicId = 202 });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Theory]
    [InlineData(null, null, Password)]
    [InlineData("bad", null, Password)]
    [InlineData(null, "123", Password)]
    [InlineData("a@example.invalid", null, "short")]
    public async Task Invalid_registration_is_rejected(string? email, string? phone, string password)
    {
        using var app = new PlatformApiFactory(); await app.SeedAsync(); using var client = app.Client();
        using var response = await client.PostAsJsonAsync(Root + "register", new PlatformRegisterRequest("Synthetic", email, phone, password));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Empty(app.Messages.Messages);
    }

    [Fact]
    public async Task BCrypt_byte_limit_rejects_multibyte_truncation()
    {
        using var app = new PlatformApiFactory(); await app.SeedAsync(); using var client = app.Client();
        using var response = await client.PostAsJsonAsync(Root + "register", new PlatformRegisterRequest("Synthetic", "a@example.invalid", null, new string('\u0416', 40)));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Platform_and_legacy_tokens_are_not_interchangeable()
    {
        using var app = new PlatformApiFactory(); await app.SeedAsync(); using var client = app.Client();
        await Register(app, client); await Verify(app, client); var login = await Login(client);
        using var platform = Authorized(app, login.Token); using var legacy = await app.AsAsync(11);
        using var denied = await legacy.GetAsync(Root + "me");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        foreach (var url in new[] { "/api/Auth/me", "/api/Patients/me", "/api/admin/Doctors", "/api/Clinic/me/location" })
        {
            using var response = await platform.GetAsync(url);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        using var anonymous = await client.GetAsync(Root + "me"); Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using var legacyStillWorks = await legacy.GetAsync("/api/Patients/me"); Assert.Equal(HttpStatusCode.OK, legacyStillWorks.StatusCode);
        var pieces = login.Token.Split('.'); var payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(Convert.FromBase64String(pieces[1].Replace('-', '+').Replace('_', '/').PadRight((pieces[1].Length + 3) / 4 * 4, '=')))!;
        Assert.False(payload.ContainsKey("clinicId")); Assert.False(payload.ContainsKey("role"));
        var changed = Encoding.UTF8.GetBytes("{\"sub\":\"999\",\"accountId\":\"999\",\"identityType\":\"platform\"}");
        pieces[1] = Convert.ToBase64String(changed).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        using var tampered = Authorized(app, string.Join('.', pieces));
        using var invalid = await tampered.GetAsync(Root + "me"); Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
    }

    [Fact]
    public async Task Ownership_is_token_bound_and_reset_revokes_only_own_platform_tokens()
    {
        using var app = new PlatformApiFactory(); await app.SeedAsync(); using var client = app.Client();
        await Register(app, client); await Verify(app, client); var a = await Login(client);
        await Register(app, client, "b@example.invalid"); await Verify(app, client, "b@example.invalid"); var b = await Login(client, "b@example.invalid");
        using var ca = Authorized(app, a.Token); using var cb = Authorized(app, b.Token); using var legacy = await app.AsAsync(11);
        var account = await ca.GetFromJsonAsync<PlatformAccountResponse>(Root + "me?accountId=" + b.Account.Id);
        Assert.Equal(a.Account.Id, account!.Id);
        using var forgot = await client.PostAsJsonAsync(Root + "forgot-password", new PlatformIdentifierRequest("a@example.invalid"));
        Assert.Equal(HttpStatusCode.Accepted, forgot.StatusCode);
        var code = app.Messages.Code("a@example.invalid", "password-reset");
        using var reset = await client.PostAsJsonAsync(Root + "reset-password", new PlatformResetPasswordRequest("a@example.invalid", code, Changed));
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        using var replay = await client.PostAsJsonAsync(Root + "reset-password", new PlatformResetPasswordRequest("a@example.invalid", code, Password));
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        using var revoked = await ca.GetAsync(Root + "me"); Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        using var unaffected = await cb.GetAsync(Root + "me"); Assert.Equal(HttpStatusCode.OK, unaffected.StatusCode);
        using var clinicUnaffected = await legacy.GetAsync("/api/Patients/me"); Assert.Equal(HttpStatusCode.OK, clinicUnaffected.StatusCode);
        _ = await Login(client, password: Changed);
        using var old = await client.PostAsJsonAsync(Root + "login", new PlatformLoginRequest("a@example.invalid", Password));
        Assert.Equal(HttpStatusCode.Unauthorized, old.StatusCode);
    }

    [Fact]
    public async Task Verification_attempt_limit_expiry_resend_and_one_time_consumption_are_enforced()
    {
        using var app = new PlatformApiFactory(); await app.SeedAsync(); using var client = app.Client();
        await Register(app, client); var firstCode = app.Messages.Code("a@example.invalid");
        var wrong = firstCode == "000000" ? "111111" : "000000";
        for (var i = 0; i < 5; i++)
        {
            using var result = await client.PostAsJsonAsync(Root + "verify", new PlatformVerifyRequest("a@example.invalid", wrong));
            Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        }
        using var blocked = await client.PostAsJsonAsync(Root + "verify", new PlatformVerifyRequest("a@example.invalid", firstCode));
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.PlatformVerificationCodes.ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedAt, DateTime.UtcNow.AddMinutes(-2)));
        using var resend = await client.PostAsJsonAsync(Root + "resend-verification", new PlatformIdentifierRequest("a@example.invalid"));
        Assert.Equal(HttpStatusCode.Accepted, resend.StatusCode);
        Assert.Equal(2, app.Messages.Messages.Count);
        var latest = await db.PlatformVerificationCodes.OrderByDescending(x => x.Id).FirstAsync();
        await db.PlatformVerificationCodes.Where(x => x.Id == latest.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, DateTime.UtcNow.AddSeconds(-1)));
        using var expired = await client.PostAsJsonAsync(Root + "verify", new PlatformVerifyRequest("a@example.invalid", app.Messages.Code("a@example.invalid")));
        Assert.Equal(HttpStatusCode.BadRequest, expired.StatusCode);
    }

    [Fact]
    public async Task Unknown_recovery_and_resend_are_generic_and_do_not_send_messages()
    {
        using var app = new PlatformApiFactory(); await app.SeedAsync(); using var client = app.Client();
        foreach (var endpoint in new[] { "forgot-password", "resend-verification" })
        {
            using var response = await client.PostAsJsonAsync(Root + endpoint, new PlatformIdentifierRequest("unknown@example.invalid"));
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }
        Assert.Empty(app.Messages.Messages);
    }

    [Fact]
    public async Task Deactivated_account_token_is_rejected()
    {
        using var app = new PlatformApiFactory(); await app.SeedAsync(); using var client = app.Client();
        await Register(app, client); await Verify(app, client); var login = await Login(client);
        using var token = Authorized(app, login.Token);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.PlatformAccounts.ExecuteUpdateAsync(s => s.SetProperty(a => a.IsActive, false));
        using var response = await token.GetAsync(Root + "me"); Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("INSERT INTO ClinicPatientLinks (PlatformAccountId,ClinicId,PatientId,CreatedAt) VALUES (1,101,42,'2026-01-01')", 787)]
    [InlineData("INSERT INTO ClinicPatientLinks (PlatformAccountId,ClinicId,PatientId,CreatedAt) VALUES (999,101,41,'2026-01-01')", 787)]
    public async Task Link_FKs_reject_invalid_account_or_tenant(string sql, int expected)
    {
        using var app = new PlatformApiFactory(); await app.SeedAsync();
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.PlatformAccounts.Add(new PlatformAccount { Id = 1, FullName = "Synthetic", Email = "a@example.invalid", PasswordHash = "synthetic-only" });
        await db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync(sql));
        Assert.Equal(expected, error.SqliteExtendedErrorCode);
    }
}
