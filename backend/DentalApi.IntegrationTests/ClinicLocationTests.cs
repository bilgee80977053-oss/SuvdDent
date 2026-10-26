using System.Net;
using System.Net.Http.Json;
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

public sealed class ClinicLocationTests
{
    private const string LocationUrl = "/api/Clinic/me/location";
    private const string ConfirmUrl = "/api/Clinic/me/timezone-confirmation";
    private static LocationSelection Selection(string province = "11", string district = "1101") => new("MN", province, district);
    private static async Task<ClinicLocationResponse> Read(HttpClient client)
    {
        using var response = await client.GetAsync(LocationUrl);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ClinicLocationResponse>())!;
    }
    private static async Task<ClinicLocationResponse> Set(HttpClient client, LocationSelection? input = null)
    {
        var original = await Read(client);
        using var response = await client.PutAsJsonAsync(LocationUrl,
            new UpdateClinicLocationRequest(original.Version, input ?? Selection(), "Synthetic address"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ClinicLocationResponse>())!;
    }
    private static async Task<ClinicLocationResponse> Verify(HttpClient client, LocationSelection? input = null, string zone = "Asia/Ulaanbaatar")
    {
        var pending = await Set(client, input);
        using var response = await client.PostAsJsonAsync(ConfirmUrl, new ConfirmClinicTimeZoneRequest(pending.Version, zone));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ClinicLocationResponse>())!;
    }

    [Fact]
    public async Task Catalog_covers_all_provinces_and_districts_and_suggestions_never_verify()
    {
        using var app = new DoctorApiFactory(); await app.SeedAsync();
        using var client = await app.AsAsync(11);
        using var catalog = await client.GetAsync("/api/locations");
        Assert.Equal(HttpStatusCode.OK, catalog.StatusCode);
        var json = await catalog.Content.ReadFromJsonAsync<JsonElement>();
        var provinces = json.GetProperty("provinces").Deserialize<ProvinceEntry[]>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(22, provinces.Length);
        var codes = new HashSet<string>();
        foreach (var province in provinces)
        {
            var districts = (await client.GetFromJsonAsync<DistrictEntry[]>($"/api/locations/{province.Code}/districts"))!;
            Assert.NotEmpty(districts);
            if (province.Code == "11") Assert.Equal(9, districts.Length);
            foreach (var district in districts)
            {
                Assert.True(codes.Add(district.Code));
                Assert.Equal(province.Code, district.ProvinceOrCapitalCode);
                using var suggestion = await client.PostAsJsonAsync("/api/locations/timezone-suggestion", Selection(province.Code, district.Code));
                Assert.Equal(HttpStatusCode.OK, suggestion.StatusCode);
                var body = await suggestion.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal(province.Code is "83" or "84" or "85" ? "Asia/Hovd" : "Asia/Ulaanbaatar", body.GetProperty("suggestedTimeZoneId").GetString());
                Assert.True(body.GetProperty("requiresDirectorConfirmation").GetBoolean());
            }
        }
        Assert.Equal(339, codes.Count);
        using var scope = app.Services.CreateScope();
        Assert.All(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Clinics.ToArrayAsync(),
            c => Assert.Equal(TimeZoneStatus.PendingVerification, c.TimeZoneStatus));
    }

    [Fact]
    public async Task Confirmation_is_explicit_and_changed_location_invalidates_it()
    {
        using var app = new DoctorApiFactory(); await app.SeedAsync();
        using var client = await app.AsAsync(15);
        var original = await Read(client);
        Assert.Null(original.EffectiveTimeZoneId);
        using var premature = await client.PostAsJsonAsync(ConfirmUrl, new ConfirmClinicTimeZoneRequest(original.Version, "Asia/Ulaanbaatar"));
        Assert.Equal(HttpStatusCode.BadRequest, premature.StatusCode);
        var verified = await Verify(client);
        Assert.Equal(TimeZoneStatus.Verified, verified.TimeZoneStatus);
        Assert.Equal("Asia/Ulaanbaatar", verified.EffectiveTimeZoneId);
        Assert.NotNull(verified.TimeZoneVerifiedAtUtc);
        using var unchanged = await client.PutAsJsonAsync(LocationUrl, new UpdateClinicLocationRequest(verified.Version, Selection(), "Synthetic address"));
        var same = (await unchanged.Content.ReadFromJsonAsync<ClinicLocationResponse>())!;
        Assert.Equal(TimeZoneStatus.Verified, same.TimeZoneStatus);
        using var stale = await client.PostAsJsonAsync(ConfirmUrl, new ConfirmClinicTimeZoneRequest(verified.Version, "Asia/Ulaanbaatar"));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var moved = await Set(client, Selection("84", "8401"));
        Assert.Equal(TimeZoneStatus.PendingVerification, moved.TimeZoneStatus);
        Assert.Equal("Asia/Hovd", moved.SuggestedTimeZoneId);
        Assert.Null(moved.EffectiveTimeZoneId);
        Assert.Null(moved.TimeZoneVerifiedAtUtc);
        using var scope = app.Services.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Clinics.SingleAsync(c => c.Id == 101);
        Assert.Null(row.TimeZoneVerifiedByUserId);
        Assert.Null(row.TimeZoneVerifiedClinicId);
    }

    [Theory]
    [InlineData("11", "8401", "MN")]
    [InlineData("99", "9901", "MN")]
    [InlineData("11", "1101", "US")]
    [InlineData("11", "110101", "MN")]
    public async Task Invalid_location_pairs_cannot_be_saved_or_suggested(string province, string district, string country)
    {
        using var app = new DoctorApiFactory(); await app.SeedAsync();
        using var client = await app.AsAsync(15);
        var before = await Read(client);
        var selection = new LocationSelection(country, province, district);
        using var result = await client.PutAsJsonAsync(LocationUrl, new UpdateClinicLocationRequest(before.Version, selection, "Synthetic"));
        using var suggestion = await client.PostAsJsonAsync("/api/locations/timezone-suggestion", selection);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, suggestion.StatusCode);
        Assert.Equal(before, await Read(client));
    }

    [Theory]
    [InlineData("Asia/Hovd")]
    [InlineData("UTC")]
    [InlineData("Unknown/Zone")]
    public async Task Wrong_or_unsupported_zone_cannot_be_confirmed(string zone)
    {
        using var app = new DoctorApiFactory(); await app.SeedAsync();
        using var client = await app.AsAsync(15);
        var pending = await Set(client);
        using var response = await client.PostAsJsonAsync(ConfirmUrl, new ConfirmClinicTimeZoneRequest(pending.Version, zone));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(pending, await Read(client));
    }

    [Theory]
    [InlineData(11)]
    [InlineData(13)]
    [InlineData(16)]
    public async Task Non_director_roles_cannot_manage_location(int id)
    {
        using var app = new DoctorApiFactory(); await app.SeedAsync();
        using var client = await app.AsAsync(id);
        using var get = await client.GetAsync(LocationUrl);
        using var put = await client.PutAsJsonAsync(LocationUrl, new UpdateClinicLocationRequest(Guid.NewGuid(), Selection(), "Synthetic"));
        using var post = await client.PostAsJsonAsync(ConfirmUrl, new ConfirmClinicTimeZoneRequest(Guid.NewGuid(), "Asia/Ulaanbaatar"));
        Assert.Equal(HttpStatusCode.Forbidden, get.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);
    }

    [Fact]
    public async Task Anonymous_requests_require_authentication()
    {
        using var app = new DoctorApiFactory(); await app.SeedAsync();
        using var client = app.Client();
        foreach (var url in new[] { LocationUrl, "/api/locations" })
        {
            using var response = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        using var put = await client.PutAsJsonAsync(LocationUrl, new { });
        using var post = await client.PostAsJsonAsync(ConfirmUrl, new { });
        Assert.Equal(HttpStatusCode.Unauthorized, put.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, post.StatusCode);
    }

    [Fact]
    public async Task Director_ownership_comes_from_JWT_despite_forged_body_query_and_header()
    {
        using var app = new DoctorApiFactory(); await app.SeedAsync();
        using var a = await app.AsAsync(15);
        using var b = await app.AsAsync(25, 202);
        var originalB = await Read(b);
        var originalA = await Read(a);
        a.DefaultRequestHeaders.Add("X-Clinic-Id", "202");
        using var put = await a.PutAsJsonAsync(LocationUrl + "?clinicId=202", new
        {
            clinicId = 202, userId = 25, version = originalA.Version, location = Selection(), addressLine = "Synthetic address",
            timeZoneStatus = "Verified", timeZoneVerifiedByUserId = 25
        });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var pending = await Read(a);
        Assert.Equal(TimeZoneStatus.PendingVerification, pending.TimeZoneStatus);
        using var confirm = await a.PostAsJsonAsync(ConfirmUrl + "?clinicId=202", new
        {
            clinicId = 202, userId = 25, version = pending.Version, timeZoneId = "Asia/Ulaanbaatar"
        });
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        Assert.Equal(originalB, await Read(b));
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Clinics.SingleAsync(c => c.Id == 101);
        Assert.Equal(15, row.TimeZoneVerifiedByUserId);
        Assert.Equal(101, row.TimeZoneVerifiedClinicId);
    }

    [Fact]
    public async Task Concurrent_location_updates_cannot_both_win()
    {
        using var app = new DoctorApiFactory(); await app.SeedAsync();
        using var a = await app.AsAsync(15);
        using var b = app.Client(); b.DefaultRequestHeaders.Authorization = a.DefaultRequestHeaders.Authorization;
        var original = await Read(a);
        var responses = await Task.WhenAll(
            a.PutAsJsonAsync(LocationUrl, new UpdateClinicLocationRequest(original.Version, Selection(), "Left")),
            b.PutAsJsonAsync(LocationUrl, new UpdateClinicLocationRequest(original.Version, Selection("84", "8401"), "Right")));
        try
        {
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        }
        finally { foreach (var r in responses) r.Dispose(); }
    }

    [Fact]
    public async Task Policy_fails_closed_and_uses_each_verified_clinics_zone_without_changing_UTC_data()
    {
        using var app = new DoctorApiFactory(); await app.SeedAsync();
        using var a = await app.AsAsync(15);
        using var b = await app.AsAsync(25, 202);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var policy = scope.ServiceProvider.GetRequiredService<ClinicSchedulingPolicy>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => policy.RequireVerifiedAsync(101));
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            var pending = await policy.RequireVerifiedAsync(101);
            Assert.Equal(409, pending.Status);
            Assert.Equal("CLINIC_TIMEZONE_UNVERIFIED", pending.ErrorCode);
        }
        var verifiedA = await Verify(a);
        var verifiedB = await Verify(b, Selection("84", "8401"), "Asia/Hovd");
        var patientsBefore = await app.SnapshotAsync(202);
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            var za = await policy.RequireVerifiedAsync(101, verifiedA.Version);
            var zb = await policy.RequireVerifiedAsync(202, verifiedB.Version);
            Assert.Equal(200, za.Status); Assert.Equal(200, zb.Status);
            var local = new DateTime(2026, 11, 1, 9, 0, 0, DateTimeKind.Unspecified);
            Assert.Equal(1, TimeZoneInfo.ConvertTimeToUtc(local, za.TimeZone!).Hour);
            Assert.Equal(2, TimeZoneInfo.ConvertTimeToUtc(local, zb.TimeZone!).Hour);
            Assert.Equal("CLINIC_SCHEDULING_VERSION_CONFLICT", (await policy.RequireVerifiedAsync(101, Guid.NewGuid())).ErrorCode);
        }
        Assert.Equal(patientsBefore, await app.SnapshotAsync(202));
        await Set(a, Selection("84", "8401"));
        await using (var tx = await db.Database.BeginTransactionAsync())
            Assert.Equal(ClinicSchedulingPolicy.Unverified, (await policy.RequireVerifiedAsync(101)).ErrorCode);
    }

    [Fact]
    public async Task Database_rejects_cross_tenant_verifier_and_incomplete_verification()
    {
        using var app = new DoctorApiFactory(); await app.SeedAsync();
        using var a = await app.AsAsync(15); await Verify(a);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var sql in new[]
        {
            "UPDATE Clinics SET TimeZoneVerifiedByUserId=25 WHERE Id=101",
            "UPDATE Clinics SET TimeZoneVerifiedClinicId=202,TimeZoneVerifiedByUserId=25 WHERE Id=101",
            "UPDATE Clinics SET TimeZoneVerifiedAtUtc=NULL WHERE Id=101",
            "UPDATE Clinics SET TimeZoneStatus='Unrecognized' WHERE Id=101",
            "DELETE FROM Users WHERE Id=15"
        })
        {
            var error = await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync(sql));
            Assert.Contains(error.SqliteExtendedErrorCode, new[] { 275, 787, 1811 });
        }
    }
}
