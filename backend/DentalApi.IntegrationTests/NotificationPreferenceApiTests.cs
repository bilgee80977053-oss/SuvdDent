using System.Net;
using System.Net.Http.Json;
using DentalApi.Contracts;
using DentalApi.Data;
using DentalApi.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DentalApi.IntegrationTests;

public sealed class NotificationPreferenceApiTests
{
    private const string Url = "/api/notification-preferences/me";
    private static async Task<DoctorApiFactory> App()
    {
        var app = new DoctorApiFactory();
        await app.SeedAsync();
        return app;
    }
    private static async Task<NotificationPreferencesResponse> Put(HttpClient client, bool email, bool sms)
    {
        using var response = await client.PutAsJsonAsync(Url, new { emailEnabled = email, smsEnabled = sms });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<NotificationPreferencesResponse>())!;
    }

    [Fact]
    public async Task Missing_preferences_default_false_without_inserting_and_contacts_are_separate()
    {
        using var app = await App(); using var client = await app.AsAsync(11);
        var result = await client.GetFromJsonAsync<NotificationPreferencesResponse>(Url);
        Assert.Equal(new(false, false, true, false), result);
        using var scope = app.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<NotificationPreference>().ToArrayAsync());
    }

    [Fact]
    public async Task Put_creates_updates_and_withdraws_without_verifying_contact()
    {
        using var app = await App(); using var client = await app.AsAsync(11);
        Assert.Equal(new(true, true, true, false), await Put(client, true, true));
        Assert.Equal(new(false, true, true, false), await Put(client, false, true));
        Assert.Equal(new(false, false, true, false), await Put(client, false, false));
        Assert.Equal(new(false, false, true, false), await client.GetFromJsonAsync<NotificationPreferencesResponse>(Url));
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Single(await db.Set<NotificationPreference>().ToArrayAsync());
        Assert.False((await db.Users.SingleAsync(x => x.Id == 11)).IsPhoneVerified);
    }

    [Theory]
    [InlineData(0, 401)] [InlineData(13, 403)] [InlineData(15, 403)] [InlineData(16, 403)]
    public async Task Only_authenticated_patients_can_read_or_write(int user, int status)
    {
        using var app = await App();
        using var client = user == 0 ? app.Client() : await app.AsAsync(user);
        using var read = await client.GetAsync(Url);
        using var write = await client.PutAsJsonAsync(Url, new { emailEnabled = true, smsEnabled = true });
        Assert.Equal((HttpStatusCode)status, read.StatusCode);
        Assert.Equal((HttpStatusCode)status, write.StatusCode);
    }

    [Fact]
    public async Task Body_and_query_ids_cannot_target_other_clinic_or_user_or_set_verification()
    {
        using var app = await App(); using var a = await app.AsAsync(11); using var b = await app.AsAsync(12, 202);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Users.Add(new User { Id = 27, ClinicId = 101, Role = Role.Patient, IsActive = true,
                Email = "user27@example.invalid", IsEmailVerified = true,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(DentalApiFactory.Password, workFactor: 4) });
            await db.SaveChangesAsync();
        }
        using var other = await app.AsAsync(27);
        await Put(b, true, false);
        await Put(other, false, true);
        using var response = await a.PutAsJsonAsync(Url + "?clinicId=202&userId=12",
            new { emailEnabled = true, smsEnabled = true, clinicId = 202, userId = 12,
                isPhoneVerified = true, isEmailVerified = false });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new(true, true, true, false), await response.Content.ReadFromJsonAsync<NotificationPreferencesResponse>());
        Assert.Equal(new(true, false, true, false), await b.GetFromJsonAsync<NotificationPreferencesResponse>(Url));
        Assert.Equal(new(false, true, true, false), await other.GetFromJsonAsync<NotificationPreferencesResponse>(Url));
        Assert.Equal(new(true, true, true, false),
            await a.GetFromJsonAsync<NotificationPreferencesResponse>(Url + "?clinicId=202&userId=12"));
    }

    [Theory]
    [InlineData("""{}""")] [InlineData("""{"emailEnabled":true}""")]
    [InlineData("""{"emailEnabled":null,"smsEnabled":true}""")]
    [InlineData("""{"emailEnabled":"true","smsEnabled":true}""")]
    public async Task Incomplete_or_invalid_updates_do_not_silently_withdraw_consent(string json)
    {
        using var app = await App(); using var client = await app.AsAsync(11);
        await Put(client, true, true);
        using var body = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        using var response = await client.PutAsync(Url, body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(new(true, true, true, false), await client.GetFromJsonAsync<NotificationPreferencesResponse>(Url));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Concurrent_puts_create_one_row_and_never_mix_flag_pairs(bool existing)
    {
        using var app = await App(); using var a = await app.AsAsync(11); using var b = await app.AsAsync(11);
        if (existing) await Put(a, false, false);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<NotificationPreferencesResponse> Send(HttpClient client, bool email, bool sms)
        {
            await gate.Task;
            return await Put(client, email, sms);
        }
        var first = Task.Run(() => Send(a, true, false));
        var second = Task.Run(() => Send(b, false, true));
        gate.SetResult();
        var results = await Task.WhenAll(first, second);
        Assert.All(results, x => Assert.NotEqual(x.EmailEnabled, x.SmsEnabled));
        var final = await a.GetFromJsonAsync<NotificationPreferencesResponse>(Url);
        Assert.NotEqual(final!.EmailEnabled, final.SmsEnabled);
        using var scope = app.Services.CreateScope();
        Assert.Single(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<NotificationPreference>().ToArrayAsync());
    }
}
