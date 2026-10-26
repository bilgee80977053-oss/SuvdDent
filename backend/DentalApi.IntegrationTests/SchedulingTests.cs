using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DentalApi.Contracts;
using DentalApi.Data;
using DentalApi.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DentalApi.IntegrationTests;

public sealed class SchedulingTests
{
    private const string ServicesUrl = "/api/admin/Doctors/31/services";
    private const string AvailabilityUrl = "/api/Patients/me/availability";
    private static AvailabilityInput Window(int hour = 1) => new(
        new DateTimeOffset(2026, 11, 1, hour, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 11, 1, hour + 1, 0, 0, TimeSpan.Zero));
    private static DoctorServiceInput Service(int id = 51) => new(id, 30, 5, 10);
    private static async Task<DoctorApiFactory> App()
    {
        var app = new DoctorApiFactory();
        await app.SeedAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Services.AddRange(new Service { Id = 51, ClinicId = 101, Name = "Synthetic service A" },
            new Service { Id = 52, ClinicId = 101, Name = "Synthetic service A2" },
            new Service { Id = 53, ClinicId = 101, Name = "Inactive", IsActive = false },
            new Service { Id = 61, ClinicId = 202, Name = "Synthetic service B" });
        await db.SaveChangesAsync();
        return app;
    }
    private static async Task<T> Get<T>(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
    private static async Task<string> State(DoctorApiFactory app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return JsonSerializer.Serialize(new
        {
            Doctors = await db.DoctorServices.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Schedules = await db.PatientAvailabilitySchedules.AsNoTracking().OrderBy(x => x.PatientId).ToArrayAsync(),
            Intervals = await db.PatientAvailabilities.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync()
        });
    }

    [Fact]
    public async Task Director_manages_services_with_shared_profile_version_and_preserves_ids()
    {
        using var app = await App();
        using var director = await app.AsAsync(15);
        using var doctor = await app.AsAsync(13);
        var beforeB = await app.SnapshotAsync(202);
        var original = await Get<DoctorServicesResponse>(director, ServicesUrl);
        using var created = await director.PutAsJsonAsync(ServicesUrl,
            new UpdateDoctorServicesRequest(original.Version, [Service(), Service(52)]));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var current = (await created.Content.ReadFromJsonAsync<DoctorServicesResponse>())!;
        Assert.NotEqual(original.Version, current.Version);
        Assert.Equal(2, current.Services.Length);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = await db.DoctorServices.Where(x => x.ServiceId == 51).Select(x => x.Id).SingleAsync();
        using var omitted = await director.PutAsJsonAsync(ServicesUrl,
            new UpdateDoctorServicesRequest(current.Version, [new(51, 60, 0, 5)]));
        Assert.Equal(HttpStatusCode.OK, omitted.StatusCode);
        Assert.Equal(id, await db.DoctorServices.Where(x => x.ServiceId == 51).Select(x => x.Id).SingleAsync());
        Assert.False(await db.DoctorServices.Where(x => x.ServiceId == 52).Select(x => x.IsActive).SingleAsync());
        using var staleHours = await doctor.PutAsJsonAsync("/api/Doctors/me/working-hours",
            new UpdateWorkingHoursRequest(original.Version, [new(1, 600, 660)]));
        Assert.Equal(HttpStatusCode.Conflict, staleHours.StatusCode);
        Assert.Equal(beforeB, await app.SnapshotAsync(202));
    }

    [Theory]
    [InlineData(32, 51)] // Another clinic's doctor.
    [InlineData(31, 61)] // Another clinic's service.
    [InlineData(31, 53)] // Inactive service.
    [InlineData(31, 999)]
    [InlineData(34, 51)] // Inactive doctor user.
    [InlineData(35, 51)] // Inactive profile.
    public async Task Director_cannot_link_ineligible_targets(int doctor, int service)
    {
        using var app = await App();
        using var client = await app.AsAsync(15);
        var current = await Get<DoctorServicesResponse>(client, ServicesUrl);
        var before = await State(app);
        var beforeB = await app.SnapshotAsync(202);
        using var result = await client.PutAsJsonAsync($"/api/admin/Doctors/{doctor}/services",
            new UpdateDoctorServicesRequest(current.Version, [Service(service)]));
        Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
        Assert.Equal(before, await State(app));
        Assert.Equal(current.Version, (await Get<DoctorServicesResponse>(client, ServicesUrl)).Version);
        Assert.Equal(beforeB, await app.SnapshotAsync(202));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1440, 0, 1)]
    [InlineData(30, -1, 0)]
    [InlineData(30, 0, -1)]
    public async Task Invalid_duration_and_buffers_are_rejected(int duration, int before, int after)
    {
        using var app = await App();
        using var client = await app.AsAsync(15);
        var current = await Get<DoctorServicesResponse>(client, ServicesUrl);
        var state = await State(app);
        using var result = await client.PutAsJsonAsync(ServicesUrl,
            new UpdateDoctorServicesRequest(current.Version, [new(51, duration, before, after)]));
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(state, await State(app));
    }

    [Fact]
    public async Task Duplicate_service_and_empty_version_are_rejected()
    {
        using var app = await App();
        using var client = await app.AsAsync(15);
        var current = await Get<DoctorServicesResponse>(client, ServicesUrl);
        using var duplicate = await client.PutAsJsonAsync(ServicesUrl,
            new UpdateDoctorServicesRequest(current.Version, [Service(), Service()]));
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        using var missing = await client.PutAsJsonAsync(ServicesUrl,
            new UpdateDoctorServicesRequest(Guid.Empty, [Service()]));
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
    }

    [Theory]
    [InlineData(13, ServicesUrl)]
    [InlineData(11, ServicesUrl)]
    [InlineData(15, AvailabilityUrl)]
    [InlineData(13, AvailabilityUrl)]
    public async Task Wrong_roles_cannot_read_or_write(int user, string path)
    {
        using var app = await App();
        using var client = await app.AsAsync(user);
        using var read = await client.GetAsync(path);
        using var write = await client.PutAsJsonAsync(path, new { version = Guid.Empty, intervals = Array.Empty<object>(), services = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }

    [Theory]
    [InlineData(ServicesUrl)]
    [InlineData(AvailabilityUrl)]
    public async Task Anonymous_requests_require_authentication(string path)
    {
        using var app = await App();
        using var client = app.Client();
        using var read = await client.GetAsync(path);
        using var write = await client.PutAsJsonAsync(path, new { });
        Assert.Equal(HttpStatusCode.Unauthorized, read.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, write.StatusCode);
    }

    [Fact]
    public async Task Patient_owns_utc_normalized_intervals_and_empty_lists_retain_version()
    {
        using var app = await App();
        using var a = await app.AsAsync(11);
        using var b = await app.AsAsync(12, 202);
        var beforeB = await app.SnapshotAsync(202);
        var initial = await Get<AvailabilityResponse>(a, AvailabilityUrl);
        Assert.Equal(Guid.Empty, initial.Version);
        Assert.Empty(initial.Intervals);
        var content = new StringContent("{\"version\":\"00000000-0000-0000-0000-000000000000\",\"clinicId\":202,\"patientId\":42,\"userId\":12,\"intervals\":[{\"startUtc\":\"2026-11-01T09:00:00+08:00\",\"endUtc\":\"2026-11-01T10:00:00+08:00\"},{\"startUtc\":\"2026-11-01T02:00:00Z\",\"endUtc\":\"2026-11-01T03:00:00Z\"}]}", Encoding.UTF8, "application/json");
        using var put = await a.PutAsync(AvailabilityUrl + "?clinicId=202&patientId=42", content);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var current = (await put.Content.ReadFromJsonAsync<AvailabilityResponse>())!;
        Assert.Equal(Window(), current.Intervals[0]);
        Assert.All(current.Intervals, i => Assert.Equal(TimeSpan.Zero, i.StartUtc.Offset));
        Assert.Empty((await Get<AvailabilityResponse>(b, AvailabilityUrl)).Intervals);
        using var clear = await a.PutAsJsonAsync(AvailabilityUrl, new UpdateAvailabilityRequest(current.Version, []));
        Assert.Equal(HttpStatusCode.OK, clear.StatusCode);
        var cleared = (await clear.Content.ReadFromJsonAsync<AvailabilityResponse>())!;
        Assert.NotEqual(current.Version, cleared.Version);
        Assert.NotEqual(Guid.Empty, cleared.Version);
        Assert.Empty(cleared.Intervals);
        using var stale = await a.PutAsJsonAsync(AvailabilityUrl, new UpdateAvailabilityRequest(Guid.Empty, [Window()]));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(beforeB, await app.SnapshotAsync(202));
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(41, (await db.PatientAvailabilitySchedules.SingleAsync()).PatientId);
        var json = await (await a.GetAsync(AvailabilityUrl)).Content.ReadAsStringAsync();
        foreach (var forbidden in new[] { "password", "email", "phone", "userId", "clinicId", "patientId", "token" })
            Assert.DoesNotContain(forbidden, json, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("2026-11-01T01:00:00", "2026-11-01T02:00:00Z")]
    [InlineData("2026-11-01T01:00:00Z", "2026-11-01T02:00:00")]
    [InlineData("2026-11-01T01:00:00+25:00", "2026-11-01T02:00:00Z")]
    [InlineData("bad", "2026-11-01T02:00:00Z")]
    [InlineData("2026-11-01T02:00:00Z", "2026-11-01T01:00:00Z")]
    [InlineData("2026-11-01T01:00:00Z", "2026-11-01T01:00:00Z")]
    public async Task Invalid_or_offsetless_intervals_return_400(string start, string end)
    {
        using var app = await App();
        using var client = await app.AsAsync(11);
        using var response = await client.PutAsJsonAsync(AvailabilityUrl,
            new { version = Guid.Empty, intervals = new[] { new { startUtc = start, endUtc = end } } });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty((await Get<AvailabilityResponse>(client, AvailabilityUrl)).Intervals);
    }

    [Fact]
    public async Task Overlap_is_checked_by_instant_even_with_different_offsets()
    {
        using var app = await App();
        using var client = await app.AsAsync(11);
        var a = Window();
        var overlapping = new AvailabilityInput(a.StartUtc.AddMinutes(30).ToOffset(TimeSpan.FromHours(8)), a.EndUtc.AddHours(1));
        using var response = await client.PutAsJsonAsync(AvailabilityUrl,
            new UpdateAvailabilityRequest(Guid.Empty, [a, overlapping]));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(Guid.Empty, (await Get<AvailabilityResponse>(client, AvailabilityUrl)).Version);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Concurrent_updates_with_same_version_have_exactly_one_winner(bool services)
    {
        using var app = await App();
        using var first = await app.AsAsync(services ? 15 : 11);
        using var second = app.Client();
        second.DefaultRequestHeaders.Authorization = first.DefaultRequestHeaders.Authorization;
        var version = services ? (await Get<DoctorServicesResponse>(first, ServicesUrl)).Version : Guid.Empty;
        object left = services ? new UpdateDoctorServicesRequest(version, [Service()]) : new UpdateAvailabilityRequest(version, [Window()]);
        object right = services ? new UpdateDoctorServicesRequest(version, [Service(52)]) : new UpdateAvailabilityRequest(version, [Window(3)]);
        var path = services ? ServicesUrl : AvailabilityUrl;
        var results = await Task.WhenAll(first.PutAsJsonAsync(path, left), second.PutAsJsonAsync(path, right));
        try
        {
            Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
            Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
            if (services) Assert.Single((await Get<DoctorServicesResponse>(first, path)).Services);
            else Assert.Single((await Get<AvailabilityResponse>(first, path)).Intervals);
        }
        finally { foreach (var response in results) response.Dispose(); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Missing_or_inactive_patient_returns_404(bool inactive)
    {
        using var app = await App();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (inactive) await db.Patients.Where(p => p.Id == 41).ExecuteUpdateAsync(s => s.SetProperty(p => p.IsActive, false));
            else await db.Patients.Where(p => p.Id == 41).ExecuteDeleteAsync();
        }
        using var client = await app.AsAsync(11);
        using var get = await client.GetAsync(AvailabilityUrl);
        using var put = await client.PutAsJsonAsync(AvailabilityUrl, new UpdateAvailabilityRequest(Guid.Empty, [Window()]));
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
    }

    [Theory]
    [InlineData("INSERT INTO DoctorServices (ClinicId,DoctorProfileId,ServiceId,DurationMinutes,BufferBeforeMinutes,BufferAfterMinutes,IsActive) VALUES (101,32,51,30,0,0,1)", 787)]
    [InlineData("INSERT INTO DoctorServices (ClinicId,DoctorProfileId,ServiceId,DurationMinutes,BufferBeforeMinutes,BufferAfterMinutes,IsActive) VALUES (101,31,61,30,0,0,1)", 787)]
    [InlineData("INSERT INTO PatientAvailabilitySchedules (ClinicId,PatientId,Version) VALUES (101,42,'test')", 787)]
    [InlineData("INSERT INTO PatientAvailabilities (ClinicId,PatientId,StartUtc,EndUtc) VALUES (101,42,1,2)", 787)]
    [InlineData("INSERT INTO DoctorServices (ClinicId,DoctorProfileId,ServiceId,DurationMinutes,BufferBeforeMinutes,BufferAfterMinutes,IsActive) VALUES (101,31,51,0,0,0,1)", 275)]
    [InlineData("INSERT INTO DoctorServices (ClinicId,DoctorProfileId,ServiceId,DurationMinutes,BufferBeforeMinutes,BufferAfterMinutes,IsActive) VALUES (101,31,51,1440,1,0,1)", 275)]
    public async Task Database_enforces_tenant_and_duration_constraints(string sql, int code)
    {
        using var app = await App();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var error = await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync(sql));
        Assert.Equal(code, error.SqliteExtendedErrorCode);
    }
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"startUtc\":\"2026-11-01T01:00:00Z\"}")]
    [InlineData("{\"endUtc\":\"2026-11-01T02:00:00Z\"}")]
    [InlineData("null")]
    public async Task Missing_timestamp_fields_and_null_intervals_return_400(string interval)
    {
        using var app = await App();
        using var client = await app.AsAsync(11);
        using var body = new StringContent("{\"version\":\"00000000-0000-0000-0000-000000000000\",\"intervals\":[" + interval + "]}", Encoding.UTF8, "application/json");
        using var result = await client.PutAsync(AvailabilityUrl, body);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(Guid.Empty, (await Get<AvailabilityResponse>(client, AvailabilityUrl)).Version);
    }

    [Fact]
    public async Task Both_patients_update_only_their_own_schedule_and_stale_existing_version_is_rejected()
    {
        using var app = await App();
        using var a = await app.AsAsync(11);
        using var b = await app.AsAsync(12, 202);
        using var first = await a.PutAsJsonAsync(AvailabilityUrl, new UpdateAvailabilityRequest(Guid.Empty, [Window()]));
        var original = (await first.Content.ReadFromJsonAsync<AvailabilityResponse>())!;
        using var bWrite = await b.PutAsJsonAsync(AvailabilityUrl, new UpdateAvailabilityRequest(Guid.Empty, [Window(4)]));
        Assert.Equal(HttpStatusCode.OK, bWrite.StatusCode);
        Assert.Equal(Window(), Assert.Single((await Get<AvailabilityResponse>(a, AvailabilityUrl)).Intervals));
        var beforeB = await Get<AvailabilityResponse>(b, AvailabilityUrl);
        using var next = await a.PutAsJsonAsync(AvailabilityUrl, new UpdateAvailabilityRequest(original.Version, [Window(2)]));
        Assert.Equal(HttpStatusCode.OK, next.StatusCode);
        using var stale = await a.PutAsJsonAsync(AvailabilityUrl, new UpdateAvailabilityRequest(original.Version, []));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var afterB = await Get<AvailabilityResponse>(b, AvailabilityUrl);
        Assert.Equal(beforeB.Version, afterB.Version);
        Assert.Equal(beforeB.Intervals, afterB.Intervals);
        Assert.Equal(Window(2), Assert.Single((await Get<AvailabilityResponse>(a, AvailabilityUrl)).Intervals));
    }

    [Fact]
    public async Task Database_rejects_duplicate_services_invalid_availability_and_restricted_deletes()
    {
        using var app = await App();
        using var patient = await app.AsAsync(11);
        using var director = await app.AsAsync(15);
        var doctor = await Get<DoctorServicesResponse>(director, ServicesUrl);
        using var add = await director.PutAsJsonAsync(ServicesUrl, new UpdateDoctorServicesRequest(doctor.Version, [Service()]));
        Assert.Equal(HttpStatusCode.OK, add.StatusCode);
        using var availability = await patient.PutAsJsonAsync(AvailabilityUrl, new UpdateAvailabilityRequest(Guid.Empty, [Window()]));
        Assert.Equal(HttpStatusCode.OK, availability.StatusCode);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var (sql, codes) in new[]
        {
            ("INSERT INTO DoctorServices (ClinicId,DoctorProfileId,ServiceId,DurationMinutes,BufferBeforeMinutes,BufferAfterMinutes,IsActive) VALUES (101,31,51,30,0,0,1)", new[] { 2067 }),
            ("INSERT INTO PatientAvailabilities (ClinicId,PatientId,StartUtc,EndUtc) VALUES (101,41,2,1)", new[] { 275 }),
            ("DELETE FROM Services WHERE Id=51", new[] { 787, 1811 }),
            ("DELETE FROM DoctorProfiles WHERE Id=31", new[] { 787, 1811 }),
            ("DELETE FROM Patients WHERE Id=41", new[] { 787, 1811 }),
            ("UPDATE PatientAvailabilities SET ClinicId=202 WHERE PatientId=41", new[] { 787 })
        })
        {
            var error = await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync(sql));
            Assert.Contains(error.SqliteExtendedErrorCode, codes);
        }
    }

    [Fact]
    public async Task Director_B_is_scoped_by_JWT_despite_forged_body_query_and_headers()
    {
        using var app = await App();
        using var director = await app.AsAsync(25, 202);
        director.DefaultRequestHeaders.Add("X-Clinic-Id", "101");
        using var forbidden = await director.GetAsync(ServicesUrl + "?clinicId=101");
        Assert.Equal(HttpStatusCode.NotFound, forbidden.StatusCode);
        var b = await Get<DoctorServicesResponse>(director, "/api/admin/Doctors/32/services");
        using var result = await director.PutAsJsonAsync("/api/admin/Doctors/32/services?clinicId=101", new
        {
            version = b.Version, clinicId = 101, doctorProfileId = 31,
            services = new[] { Service(61) }
        });
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.DoctorServices.SingleAsync();
        Assert.Equal(202, row.ClinicId);
        Assert.Equal(32, row.DoctorProfileId);
    }

}
