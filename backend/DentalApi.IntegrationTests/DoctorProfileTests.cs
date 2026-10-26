using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DentalApi.Contracts;
using DentalApi.Data;
using DentalApi.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DentalApi.IntegrationTests;

public sealed class DoctorProfileTests
{
    private static DoctorDetails Details(string specialty = "Orthodontics") => new(specialty, 2010,
        "Synthetic biography", "/uploads/doctors/101/synthetic.webp");

    private static async Task<DoctorResponse> ReadAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DoctorResponse>())!;
    }

    [Fact]
    public async Task Catalog_exposes_only_own_active_published_doctors_without_private_fields()
    {
        using var app = new DoctorApiFactory();
        await app.SeedAsync();
        using var client = await app.AsAsync(11);
        client.DefaultRequestHeaders.Add("X-Clinic-Id", "202");
        using var response = await client.GetAsync("/api/Doctors?clinicId=202&userId=23");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        var profiles = JsonSerializer.Deserialize<DoctorResponse[]>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(31, Assert.Single(profiles).Id);
        Assert.Equal("Synthetic user 13", profiles[0].FullName);
        foreach (var forbidden in new[] { "password", "email", "phone", "token", "userId", "clinicId" })
            Assert.DoesNotContain(forbidden, json, StringComparison.OrdinalIgnoreCase);
        foreach (var id in new[] { 32, 33, 34, 35 })
        {
            using var hidden = await client.GetAsync("/api/Doctors/" + id);
            Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        }
    }

    [Fact]
    public async Task Director_creates_only_one_profile_for_eligible_own_doctor()
    {
        using var app = new DoctorApiFactory();
        await app.SeedAsync();
        var beforeB = await app.SnapshotAsync(202);
        using var director = await app.AsAsync(15);
        using var created = await director.PostAsJsonAsync("/api/admin/Doctors?clinicId=202", new
        {
            userId = 20, clinicId = 202, profile = Details(), isPublished = true
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.NotNull(created.Headers.Location);
        var profile = (await created.Content.ReadFromJsonAsync<DoctorResponse>())!;
        Assert.Equal("Synthetic user 20", profile.FullName);
        using var fetched = await director.GetAsync(created.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        using var duplicate = await director.PostAsJsonAsync("/api/admin/Doctors", new CreateDoctorRequest(20, Details()));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(beforeB, await app.SnapshotAsync(202));
    }

    [Theory]
    [InlineData(23)] // Other clinic.
    [InlineData(11)] // Patient.
    [InlineData(15)] // Director.
    [InlineData(16)] // Nurse.
    [InlineData(21)] // Inactive doctor.
    [InlineData(999)]
    public async Task Director_cannot_create_for_ineligible_user(int userId)
    {
        using var app = new DoctorApiFactory();
        await app.SeedAsync();
        var beforeA = await app.SnapshotAsync(101);
        var beforeB = await app.SnapshotAsync(202);
        using var director = await app.AsAsync(15);
        using var response = await director.PostAsJsonAsync("/api/admin/Doctors", new CreateDoctorRequest(userId, Details()));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(beforeA, await app.SnapshotAsync(101));
        Assert.Equal(beforeB, await app.SnapshotAsync(202));
    }

    [Fact]
    public async Task Director_management_is_tenant_scoped_and_controls_publication()
    {
        using var app = new DoctorApiFactory();
        await app.SeedAsync();
        var beforeB = await app.SnapshotAsync(202);
        using var director = await app.AsAsync(15);
        var hidden = await ReadAsync(director, "/api/admin/Doctors/33");
        using var publish = await director.PutAsJsonAsync("/api/admin/Doctors/33",
            new ManageDoctorRequest(hidden.Version, Details(), true, true));
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);
        using var other = await director.GetAsync("/api/admin/Doctors/32");
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        using var updateOther = await director.PutAsJsonAsync("/api/admin/Doctors/32",
            new ManageDoctorRequest(hidden.Version, Details(), true, true));
        Assert.Equal(HttpStatusCode.NotFound, updateOther.StatusCode);
        using var hoursOther = await director.PutAsJsonAsync("/api/admin/Doctors/32/working-hours",
            new UpdateWorkingHoursRequest(hidden.Version, [new(1, 0, 1440)]));
        Assert.Equal(HttpStatusCode.NotFound, hoursOther.StatusCode);
        using var patient = await app.AsAsync(11);
        using var visible = await patient.GetAsync("/api/Doctors/33");
        Assert.Equal(HttpStatusCode.OK, visible.StatusCode);
        Assert.Equal(beforeB, await app.SnapshotAsync(202));
    }

    [Fact]
    public async Task Doctor_edits_only_own_profile_ignoring_injected_identity_and_status()
    {
        using var app = new DoctorApiFactory();
        await app.SeedAsync();
        var beforeB = await app.SnapshotAsync(202);
        using var doctor = await app.AsAsync(13);
        var original = await ReadAsync(doctor, "/api/Doctors/me");
        using var response = await doctor.PutAsJsonAsync("/api/Doctors/me?clinicId=202&userId=23&id=32", new
        {
            version = original.Version, profile = Details("Updated own specialty"),
            id = 32, clinicId = 202, userId = 23, isActive = false, isPublished = false, role = "Director"
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<DoctorResponse>())!;
        Assert.Equal(31, updated.Id);
        Assert.Equal("Updated own specialty", updated.Specialty);
        Assert.True(updated.IsActive && updated.IsPublished);
        Assert.NotEqual(original.Version, updated.Version);
        using var forbidden = await doctor.PutAsJsonAsync("/api/admin/Doctors/33",
            new ManageDoctorRequest(updated.Version, Details(), true, true));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(beforeB, await app.SnapshotAsync(202));
    }

    [Theory]
    [InlineData("/api/Doctors")]
    [InlineData("/api/Doctors/me")]
    [InlineData("/api/admin/Doctors")]
    public async Task Anonymous_requests_are_401(string path)
    {
        using var app = new DoctorApiFactory();
        await app.SeedAsync();
        using var client = app.Client();
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(11, "/api/Doctors/me")]
    [InlineData(11, "/api/admin/Doctors")]
    [InlineData(13, "/api/admin/Doctors")]
    [InlineData(15, "/api/Doctors/me")]
    public async Task Wrong_roles_are_403(int userId, string path)
    {
        using var app = new DoctorApiFactory();
        await app.SeedAsync();
        using var client = await app.AsAsync(userId);
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Valid_adjacent_hours_replace_atomically_and_stale_version_is_409()
    {
        using var app = new DoctorApiFactory();
        await app.SeedAsync();
        var beforeB = await app.SnapshotAsync(202);
        using var doctor = await app.AsAsync(13);
        var profile = await ReadAsync(doctor, "/api/Doctors/me");
        var hours = new WorkingHoursInput[] { new(1, 0, 720), new(1, 720, 1440), new(2, 540, 780) };
        using var response = await doctor.PutAsJsonAsync("/api/Doctors/me/working-hours",
            new UpdateWorkingHoursRequest(profile.Version, hours));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<DoctorResponse>())!;
        Assert.Equal(hours, updated.WorkingHours);
        using var stale = await doctor.PutAsJsonAsync("/api/Doctors/me/working-hours",
            new UpdateWorkingHoursRequest(profile.Version, []));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var stillCurrent = await ReadAsync(doctor, "/api/Doctors/me");
        Assert.Equal(hours, stillCurrent.WorkingHours);
        Assert.Equal(updated.Version, stillCurrent.Version);
        using var cleared = await doctor.PutAsJsonAsync("/api/Doctors/me/working-hours",
            new UpdateWorkingHoursRequest(updated.Version, []));
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        Assert.Empty((await cleared.Content.ReadFromJsonAsync<DoctorResponse>())!.WorkingHours);
        Assert.Equal(beforeB, await app.SnapshotAsync(202));
    }

    [Theory]
    [InlineData(1, -1, 60)]
    [InlineData(1, 60, 1441)]
    [InlineData(7, 0, 60)]
    [InlineData(1, 60, 60)]
    [InlineData(1, 120, 60)]
    public async Task Invalid_interval_is_400_and_existing_schedule_is_unchanged(int day, int start, int end)
    {
        using var app = new DoctorApiFactory();
        await app.SeedAsync();
        using var doctor = await app.AsAsync(13);
        var profile = await ReadAsync(doctor, "/api/Doctors/me");
        var before = await app.SnapshotAsync(101);
        using var response = await doctor.PutAsJsonAsync("/api/Doctors/me/working-hours",
            new UpdateWorkingHoursRequest(profile.Version, [new(day, start, end)]));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await app.SnapshotAsync(101));
    }

    [Fact]
    public async Task Overlap_is_400_and_does_not_delete_existing_schedule()
    {
        using var app = new DoctorApiFactory();
        await app.SeedAsync();
        using var director = await app.AsAsync(15);
        var profile = await ReadAsync(director, "/api/admin/Doctors/31");
        var before = await app.SnapshotAsync(101);
        using var response = await director.PutAsJsonAsync("/api/admin/Doctors/31/working-hours",
            new UpdateWorkingHoursRequest(profile.Version, [new(1, 540, 780), new(1, 600, 900)]));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await app.SnapshotAsync(101));
    }

    [Fact]
    public async Task Concurrent_schedule_updates_with_same_version_cannot_both_win()
    {
        using var app = new DoctorApiFactory();
        await app.SeedAsync();
        using var first = await app.AsAsync(13);
        using var second = app.Client();
        second.DefaultRequestHeaders.Authorization = first.DefaultRequestHeaders.Authorization;
        var profile = await ReadAsync(first, "/api/Doctors/me");
        var beforeB = await app.SnapshotAsync(202);
        var left = new WorkingHoursInput[] { new(1, 540, 600) };
        var right = new WorkingHoursInput[] { new(2, 600, 660) };
        var responses = await Task.WhenAll(
            first.PutAsJsonAsync("/api/Doctors/me/working-hours", new UpdateWorkingHoursRequest(profile.Version, left)),
            second.PutAsJsonAsync("/api/Doctors/me/working-hours", new UpdateWorkingHoursRequest(profile.Version, right)));
        try
        {
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
            var final = await ReadAsync(first, "/api/Doctors/me");
            Assert.True(final.WorkingHours.SequenceEqual(left) || final.WorkingHours.SequenceEqual(right));
            Assert.Equal(beforeB, await app.SnapshotAsync(202));
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Theory]
    [InlineData("/uploads/doctors/101/trailing.jpg\n")]
    [InlineData(" ")]
    [InlineData("https://outside.invalid/photo.jpg")]
    [InlineData("/uploads/doctors/202/foreign.jpg")]
    [InlineData("/uploads/doctors/101/../foreign.jpg")]
    public async Task Unsafe_photo_paths_are_rejected(string path)
    {
        using var app = new DoctorApiFactory();
        await app.SeedAsync();
        using var doctor = await app.AsAsync(13);
        var profile = await ReadAsync(doctor, "/api/Doctors/me");
        using var response = await doctor.PutAsJsonAsync("/api/Doctors/me",
            new UpdateDoctorRequest(profile.Version, new DoctorDetails("Dentist", 2010, null, path)));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Director_hours_and_profile_updates_share_version_and_can_deactivate()
    {
        using var app = new DoctorApiFactory();
        await app.SeedAsync();
        var beforeB = await app.SnapshotAsync(202);
        using var director = await app.AsAsync(15);
        using var doctor = await app.AsAsync(13);
        var original = await ReadAsync(doctor, "/api/Doctors/me");
        using var changedHours = await director.PutAsJsonAsync("/api/admin/Doctors/31/working-hours",
            new UpdateWorkingHoursRequest(original.Version, [new(3, 600, 900)]));
        Assert.Equal(HttpStatusCode.OK, changedHours.StatusCode);
        var current = (await changedHours.Content.ReadFromJsonAsync<DoctorResponse>())!;
        using var staleProfile = await doctor.PutAsJsonAsync("/api/Doctors/me",
            new UpdateDoctorRequest(original.Version, Details()));
        Assert.Equal(HttpStatusCode.Conflict, staleProfile.StatusCode);
        using var disable = await director.PutAsJsonAsync("/api/admin/Doctors/31",
            new ManageDoctorRequest(current.Version, Details(), false, false));
        Assert.Equal(HttpStatusCode.OK, disable.StatusCode);
        var disabled = (await disable.Content.ReadFromJsonAsync<DoctorResponse>())!;
        using var denied = await doctor.PutAsJsonAsync("/api/Doctors/me",
            new UpdateDoctorRequest(disabled.Version, Details()));
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        using var patient = await app.AsAsync(11);
        using var hidden = await patient.GetAsync("/api/Doctors/31");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.Equal(beforeB, await app.SnapshotAsync(202));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"version\":\"00000000-0000-0000-0000-000000000000\",\"intervals\":[]}")]
    [InlineData("{\"intervals\":null}")]
    [InlineData("{\"intervals\":[null]}")]
    public async Task Malformed_schedule_requests_are_400_without_changes(string json)
    {
        using var app = new DoctorApiFactory();
        await app.SeedAsync();
        var before = await app.SnapshotAsync(101);
        using var doctor = await app.AsAsync(13);
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        using var response = await doctor.PutAsync("/api/Doctors/me/working-hours", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await app.SnapshotAsync(101));
    }

    [Fact]
    public async Task Doctor_in_B_can_edit_B_without_changing_A()
    {
        using var app = new DoctorApiFactory();
        await app.SeedAsync();
        var beforeA = await app.SnapshotAsync(101);
        using var doctor = await app.AsAsync(23, 202);
        var original = await ReadAsync(doctor, "/api/Doctors/me");
        Assert.Equal(32, original.Id);
        using var update = await doctor.PutAsJsonAsync("/api/Doctors/me",
            new UpdateDoctorRequest(original.Version,
                new DoctorDetails("B specialty", 2012, "Synthetic B", "/uploads/doctors/202/b.webp")));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal(beforeA, await app.SnapshotAsync(101));
    }
    [Fact]
    public async Task Database_enforces_tenant_links_unique_doctor_and_interval_bounds()
    {
        using var app = new DoctorApiFactory();
        await app.SeedAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        async Task RejectAsync(string sql, int code)
        {
            var error = await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync(sql));
            Assert.Equal(code, error.SqliteExtendedErrorCode);
        }
        await RejectAsync("UPDATE DoctorProfiles SET ClinicId=202 WHERE Id=33", 787);
        await RejectAsync("UPDATE DoctorProfiles SET UserId=23 WHERE Id=33", 787);
        await RejectAsync("UPDATE DoctorProfiles SET UserId=13 WHERE Id=33", 2067);
        await RejectAsync("UPDATE DoctorWorkingHours SET ClinicId=202 WHERE DoctorProfileId=31", 787);
        await RejectAsync("UPDATE DoctorWorkingHours SET EndMinute=1441 WHERE DoctorProfileId=31", 275);
        await RejectAsync("DELETE FROM DoctorProfiles WHERE Id=31", 1811);
    }
}