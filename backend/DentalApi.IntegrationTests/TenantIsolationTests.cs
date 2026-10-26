using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace DentalApi.IntegrationTests;

public sealed class TenantIsolationTests
{
    [Fact]
    public async Task Patient_reads_only_own_profile_despite_foreign_query_and_headers()
    {
        using var app = new DentalApiFactory();
        using var client = app.CreateTestClient();
        await app.SeedAsync();
        var before = await app.SnapshotBAsync();
        Authenticate(client, await LoginAsync(client));
        client.DefaultRequestHeaders.Add("X-Clinic-Id", "202");
        using var response = await client.GetAsync("/api/Patients/me?clinicId=202&userId=12&id=22");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var profile = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(21, profile.GetProperty("id").GetInt32());
        Assert.Equal("Patient A", profile.GetProperty("fullName").GetString());
        Assert.Equal("Synthetic A", profile.GetProperty("address").GetString());
        Assert.False(profile.TryGetProperty("medicalNotes", out _));
        Assert.Equal(before, await app.SnapshotBAsync());
    }

    [Fact]
    public async Task Foreign_identity_in_update_cannot_modify_B_or_reassign_A()
    {
        using var app = new DentalApiFactory();
        using var client = app.CreateTestClient();
        await app.SeedAsync();
        var before = await app.SnapshotBAsync();
        Authenticate(client, await LoginAsync(client));
        client.DefaultRequestHeaders.Add("X-Clinic-Id", "202");
        using var response = await client.PutAsJsonAsync("/api/Patients/me?clinicId=202&userId=12&id=22", new
        {
            id = 22, clinicId = 202, userId = 12, role = "Director",
            fullName = "Updated A", phone = "33333333", dateOfBirth = (DateTime?)null, address = "Updated A address"
        });
        // Extra identity fields are ignored; /me updates only authenticated A.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(21, updated.GetProperty("id").GetInt32());
        Assert.Equal("Updated A", updated.GetProperty("fullName").GetString());
        Assert.Equal(before, await app.SnapshotBAsync());

        using var bClient = app.CreateTestClient();
        Authenticate(bClient, await LoginAsync(bClient, 12, 202));
        using var bResponse = await bClient.GetAsync("/api/Patients/me");
        Assert.Equal(HttpStatusCode.OK, bResponse.StatusCode);
        var bProfile = await bResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(22, bProfile.GetProperty("id").GetInt32());
        Assert.Equal("Patient B", bProfile.GetProperty("fullName").GetString());
    }

    [Theory]
    [InlineData("clinicId", false)]
    [InlineData("role", false)]
    [InlineData("clinicId", true)]
    [InlineData("role", true)]
    public async Task Tampered_or_database_mismatched_claim_is_unauthorized(string claim, bool signed)
    {
        using var app = new DentalApiFactory();
        using var client = app.CreateTestClient();
        await app.SeedAsync();
        var before = await app.SnapshotBAsync();
        var token = signed ? app.SignedTokenWithMismatch(claim) : Tamper(await LoginAsync(client), claim);
        Authenticate(client, token);
        using var response = await client.GetAsync("/api/Patients/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(before, await app.SnapshotBAsync());
    }

    [Fact]
    public async Task Anonymous_is_401_and_authenticated_wrong_role_is_403()
    {
        using var app = new DentalApiFactory();
        using var client = app.CreateTestClient();
        await app.SeedAsync();
        var before = await app.SnapshotBAsync();
        using var anonymous = await client.GetAsync("/api/Patients/me");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        Authenticate(client, await LoginAsync(client, 13));
        using var doctor = await client.GetAsync("/api/Patients/me");
        Assert.Equal(HttpStatusCode.Forbidden, doctor.StatusCode);

        Authenticate(client, await LoginAsync(client));
        using var admin = await client.GetAsync("/api/Admin/ping");
        Assert.Equal(HttpStatusCode.Forbidden, admin.StatusCode);
        Assert.Equal(before, await app.SnapshotBAsync());
    }

    [Fact]
    public async Task Reset_revokes_A_token_but_preserves_B_data_and_token()
    {
        using var app = new DentalApiFactory();
        using var client = app.CreateTestClient();
        using var bClient = app.CreateTestClient();
        await app.SeedAsync();
        var before = await app.SnapshotBAsync();
        var oldToken = await LoginAsync(client);
        Authenticate(client, oldToken);
        Authenticate(bClient, await LoginAsync(bClient, 12, 202));
        using (var initial = await client.GetAsync("/api/Patients/me"))
            Assert.Equal(HttpStatusCode.OK, initial.StatusCode);

        const string newPassword = "New-synthetic-password-456!";
        using var reset = await client.PostAsJsonAsync("/api/Auth/reset-password", new
        {
            email = "user11@example.invalid", clinicId = 101,
            code = DentalApiFactory.ResetCode, newPassword
        });
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        using var revoked = await client.GetAsync("/api/Patients/me");
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        using var bResponse = await bClient.GetAsync("/api/Patients/me");
        Assert.Equal(HttpStatusCode.OK, bResponse.StatusCode);
        Assert.Equal(before, await app.SnapshotBAsync());

        Authenticate(client, await LoginAsync(client, password: newPassword));
        using var renewed = await client.GetAsync("/api/Patients/me");
        Assert.Equal(HttpStatusCode.OK, renewed.StatusCode);

        using var oldPassword = await client.PostAsJsonAsync("/api/Auth/login", new
        {
            identifier = "user11@example.invalid", password = DentalApiFactory.Password, clinicId = 101
        });
        Assert.Equal(HttpStatusCode.Unauthorized, oldPassword.StatusCode);
    }

    private static async Task<string> LoginAsync(HttpClient client, int id = 11, int clinicId = 101,
        string password = DentalApiFactory.Password)
    {
        using var response = await client.PostAsJsonAsync("/api/Auth/login", new
        {
            identifier = $"user{id}@example.invalid", password, clinicId
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("token").GetString()!;
    }

    private static void Authenticate(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static string Tamper(string token, string claim)
    {
        var parts = token.Split('.');
        var payload = JsonNode.Parse(Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(parts[1])))!;
        payload[claim] = claim == "clinicId" ? "202" : "Director";
        parts[1] = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(payload.ToJsonString()));
        return string.Join('.', parts); // Keep original signature: verification must fail.
    }
}