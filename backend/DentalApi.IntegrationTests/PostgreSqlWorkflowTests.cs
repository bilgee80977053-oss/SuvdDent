using System.Data;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DentalApi.Contracts;
using DentalApi.Data;
using DentalApi.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace DentalApi.IntegrationTests;

[Collection("Real PostgreSQL"), Trait("Category", "PostgreSqlIntegration")]
public sealed class PostgreSqlWorkflowTests
{
    internal const string AvailabilityUrl = "/api/Patients/me/availability";
    internal static string ServicesUrl(int doctor) => $"/api/admin/Doctors/{doctor}/services";
    internal static DoctorDetails Details(string specialty = "Synthetic specialty") => new(specialty, 2015, "Synthetic biography", null);
    internal static AvailabilityInput Window(int hour = 1) => new(
        new DateTimeOffset(2026, 11, 1, hour, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 11, 1, hour + 1, 0, 0, TimeSpan.Zero));
    internal static async Task<T> Read<T>(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    [PostgreSqlFact]
    public async Task Patient_create_read_update_persists_calendar_dates_and_cannot_target_other_clinic()
    {
        await using var app = await PostgreSqlApiFactory.StartAsync();
        using var a = app.Client(app.PatientA);
        using var b = app.Client(app.PatientB);
        var beforeB = await app.SnapshotBAsync();
        using var missing = await a.GetAsync("/api/Patients/me");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        var id = await app.CreatePatientAsync();
        var created = await Read<JsonElement>(a, "/api/Patients/me");
        Assert.Equal(id, created.GetProperty("id").GetInt32());
        Assert.Equal(new DateTime(2000, 2, 3), created.GetProperty("dateOfBirth").GetDateTime());
        a.DefaultRequestHeaders.Add("X-Clinic-Id", app.ClinicB.Id.ToString());
        using var update = await a.PutAsJsonAsync($"/api/Patients/me?clinicId={app.ClinicB.Id}&userId={app.PatientB.Id}", new
        {
            id = id + 1, clinicId = app.ClinicB.Id, userId = app.PatientB.Id, role = "Director",
            fullName = " Updated synthetic patient ", phone = "99000003", dateOfBirth = "2001-04-05T00:00:00Z", address = " Updated address "
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var read = await Read<JsonElement>(a, "/api/Patients/me");
        Assert.Equal(id, read.GetProperty("id").GetInt32());
        Assert.Equal("Updated synthetic patient", read.GetProperty("fullName").GetString());
        Assert.Equal(new DateTime(2001, 4, 5), read.GetProperty("dateOfBirth").GetDateTime());
        using var duplicate = await a.PostAsJsonAsync("/api/Patients/me", new { fullName = "Duplicate", phone = "99000001" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.NotEqual(id, (await Read<JsonElement>(b, "/api/Patients/me")).GetProperty("id").GetInt32());
        Assert.Equal(beforeB, await app.SnapshotBAsync());
        await using var db = app.Context();
        var row = await db.Patients.SingleAsync(p => p.Id == id && p.ClinicId == app.ClinicA.Id);
        Assert.Equal(app.PatientA.Id, row.UserId);
        Assert.Equal(DateTimeKind.Unspecified, row.DateOfBirth!.Value.Kind);
        Assert.Equal(new DateTime(2001, 4, 5), row.DateOfBirth.Value);
        Assert.Equal(DateTimeKind.Utc, row.CreatedAt.Kind);
        using var scope = app.Services.CreateScope();
        Assert.IsType<PostgreSqlAppDbContext>(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    [PostgreSqlFact]
    public async Task Director_creates_doctor_and_doctor_updates_profile_hours_and_version()
    {
        await using var app = await PostgreSqlApiFactory.StartAsync();
        using var director = app.Client(app.DirectorA);
        using var doctor = app.Client(app.NewDoctorA);
        using var patient = app.Client(app.PatientA);
        var beforeB = await app.SnapshotBAsync();
        using var create = await director.PostAsJsonAsync("/api/admin/Doctors", new CreateDoctorRequest(app.NewDoctorA.Id, Details(), true));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var profile = (await create.Content.ReadFromJsonAsync<DoctorResponse>())!;
        Assert.Equal(app.NewDoctorA.FullName, profile.FullName);
        Assert.NotNull(create.Headers.Location);
        var fetched = await Read<DoctorResponse>(director, create.Headers.Location!.ToString());
        Assert.Equal(profile.Id, fetched.Id);
        Assert.Equal(profile.Version, fetched.Version);
        Assert.Equal(profile.Specialty, fetched.Specialty);
        Assert.Equal(profile.Id, (await Read<DoctorResponse>(doctor, "/api/Doctors/me")).Id);
        Assert.Equal(profile.Id, (await Read<DoctorResponse>(patient, $"/api/Doctors/{profile.Id}")).Id);
        using var update = await doctor.PutAsJsonAsync("/api/Doctors/me", new UpdateDoctorRequest(profile.Version, Details("Updated specialty")));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = (await update.Content.ReadFromJsonAsync<DoctorResponse>())!;
        Assert.NotEqual(profile.Version, updated.Version);
        WorkingHoursInput[] intervals = [new(1, 540, 780), new(3, 600, 900)];
        using var hours = await doctor.PutAsJsonAsync("/api/Doctors/me/working-hours", new UpdateWorkingHoursRequest(updated.Version, intervals));
        Assert.Equal(HttpStatusCode.OK, hours.StatusCode);
        var current = (await hours.Content.ReadFromJsonAsync<DoctorResponse>())!;
        Assert.Equal(intervals, current.WorkingHours);
        using var stale = await director.PutAsJsonAsync($"/api/admin/Doctors/{profile.Id}", new ManageDoctorRequest(profile.Version, Details("Stale"), true, true));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var duplicate = await director.PostAsJsonAsync("/api/admin/Doctors", new CreateDoctorRequest(app.NewDoctorA.Id, Details()));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        using var foreignCreate = await director.PostAsJsonAsync("/api/admin/Doctors", new CreateDoctorRequest(app.DoctorB.Id, Details()));
        Assert.Equal(HttpStatusCode.NotFound, foreignCreate.StatusCode);
        using var foreignRead = await director.GetAsync($"/api/admin/Doctors/{app.ProfileB.Id}");
        Assert.Equal(HttpStatusCode.NotFound, foreignRead.StatusCode);
        using var foreignWrite = await director.PutAsJsonAsync($"/api/admin/Doctors/{app.ProfileB.Id}/working-hours", new UpdateWorkingHoursRequest(app.ProfileB.Version, intervals));
        Assert.Equal(HttpStatusCode.NotFound, foreignWrite.StatusCode);
        await using var db = app.Context();
        Assert.Equal("Updated specialty", (await db.DoctorProfiles.SingleAsync(d => d.Id == profile.Id && d.ClinicId == app.ClinicA.Id)).Specialty);
        Assert.Equal(2, await db.DoctorWorkingHours.CountAsync(h => h.DoctorProfileId == profile.Id && h.ClinicId == app.ClinicA.Id));
        Assert.Equal(beforeB, await app.SnapshotBAsync());
    }

    [PostgreSqlFact]
    public async Task Authentication_and_role_restrictions_use_real_JWT_and_database_claim_validation()
    {
        await using var app = await PostgreSqlApiFactory.StartAsync();
        using var anonymous = app.Client();
        using var patient = app.Client(app.PatientA);
        using var doctor = app.Client(app.DoctorA);
        using var director = app.Client(app.DirectorA);
        foreach (var path in new[] { "/api/Patients/me", "/api/Doctors", AvailabilityUrl, ServicesUrl(app.ProfileA.Id) })
        {
            using var read = await anonymous.GetAsync(path);
            using var write = await anonymous.PutAsJsonAsync(path, new { });
            Assert.Equal(HttpStatusCode.Unauthorized, read.StatusCode);
            if (path != "/api/Doctors") Assert.Equal(HttpStatusCode.Unauthorized, write.StatusCode);
        }
        using var patientAdmin = await patient.PostAsJsonAsync("/api/admin/Doctors", new CreateDoctorRequest(app.NewDoctorA.Id, Details()));
        Assert.Equal(HttpStatusCode.Forbidden, patientAdmin.StatusCode);
        using var doctorPatient = await doctor.GetAsync("/api/Patients/me");
        Assert.Equal(HttpStatusCode.Forbidden, doctorPatient.StatusCode);
        using var patientSelfDoctor = await patient.GetAsync("/api/Doctors/me");
        Assert.Equal(HttpStatusCode.Forbidden, patientSelfDoctor.StatusCode);
        using var wrongAvailability = await director.PutAsJsonAsync(AvailabilityUrl, new UpdateAvailabilityRequest(Guid.Empty, []));
        Assert.Equal(HttpStatusCode.Forbidden, wrongAvailability.StatusCode);
        using var wrongServices = await doctor.PutAsJsonAsync(ServicesUrl(app.ProfileA.Id), new UpdateDoctorServicesRequest(app.ProfileA.Version, []));
        Assert.Equal(HttpStatusCode.Forbidden, wrongServices.StatusCode);
        var originalClinic = app.PatientA.ClinicId;
        app.PatientA.ClinicId = app.ClinicB.Id;
        using var mismatched = app.Client(app.PatientA);
        app.PatientA.ClinicId = originalClinic;
        using var invalid = await mismatched.GetAsync("/api/Patients/me");
        Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
    }

    [PostgreSqlFact]
    public async Task Doctor_services_validate_buffers_preserve_identity_and_enforce_tenants_and_versions()
    {
        await using var app = await PostgreSqlApiFactory.StartAsync();
        using var director = app.Client(app.DirectorA);
        var beforeB = await app.SnapshotBAsync();
        var path = ServicesUrl(app.ProfileA.Id);
        var initial = await Read<DoctorServicesResponse>(director, path);
        var input = new DoctorServiceInput(app.ServiceA.Id, 30, 5, 10);
        using var write = await director.PutAsJsonAsync(path, new UpdateDoctorServicesRequest(initial.Version, [input]));
        Assert.Equal(HttpStatusCode.OK, write.StatusCode);
        var current = (await write.Content.ReadFromJsonAsync<DoctorServicesResponse>())!;
        Assert.Equal(new[] { input }, current.Services);
        foreach (var invalid in new[] { new DoctorServiceInput(app.ServiceA.Id, 0, 0, 0), new(app.ServiceA.Id, 1440, 0, 1), new(app.ServiceA.Id, 30, -1, 0) })
        {
            using var rejected = await director.PutAsJsonAsync(path, new UpdateDoctorServicesRequest(current.Version, [invalid]));
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }
        using var duplicate = await director.PutAsJsonAsync(path, new UpdateDoctorServicesRequest(current.Version, [input, input]));
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        using var foreignService = await director.PutAsJsonAsync(path, new UpdateDoctorServicesRequest(current.Version, [new(app.ServiceB.Id, 30, 0, 0)]));
        Assert.Equal(HttpStatusCode.NotFound, foreignService.StatusCode);
        using var foreignDoctor = await director.GetAsync(ServicesUrl(app.ProfileB.Id));
        Assert.Equal(HttpStatusCode.NotFound, foreignDoctor.StatusCode);
        using var foreignDoctorWrite = await director.PutAsJsonAsync(ServicesUrl(app.ProfileB.Id), new UpdateDoctorServicesRequest(app.ProfileB.Version, []));
        Assert.Equal(HttpStatusCode.NotFound, foreignDoctorWrite.StatusCode);
        using var stale = await director.PutAsJsonAsync(path, new UpdateDoctorServicesRequest(initial.Version, []));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        await using var db = app.Context();
        var assignmentId = (await db.DoctorServices.SingleAsync(s => s.ClinicId == app.ClinicA.Id)).Id;
        using var clear = await director.PutAsJsonAsync(path, new UpdateDoctorServicesRequest(current.Version, []));
        Assert.Equal(HttpStatusCode.OK, clear.StatusCode);
        var stored = await db.DoctorServices.AsNoTracking().SingleAsync(s => s.ClinicId == app.ClinicA.Id);
        Assert.Equal(assignmentId, stored.Id);
        Assert.False(stored.IsActive);
        Assert.Equal(beforeB, await app.SnapshotBAsync());
    }

    [PostgreSqlFact]
    public async Task Availability_replaces_UTC_intervals_rejects_overlap_and_stale_writes_and_ignores_foreign_identity()
    {
        await using var app = await PostgreSqlApiFactory.StartAsync();
        var patientId = await app.CreatePatientAsync();
        using var a = app.Client(app.PatientA);
        using var b = app.Client(app.PatientB);
        var beforeB = await app.SnapshotBAsync();
        var initial = await Read<AvailabilityResponse>(a, AvailabilityUrl);
        Assert.Equal(Guid.Empty, initial.Version);
        using var content = new StringContent(JsonSerializer.Serialize(new
        {
            version = initial.Version, clinicId = app.ClinicB.Id, userId = app.PatientB.Id,
            intervals = new[] { new { startUtc = "2026-11-01T09:00:00.1234567+08:00", endUtc = "2026-11-01T10:00:00.1234567+08:00" } }
        }), Encoding.UTF8, "application/json");
        using var put = await a.PutAsync(AvailabilityUrl + $"?clinicId={app.ClinicB.Id}", content);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var current = (await put.Content.ReadFromJsonAsync<AvailabilityResponse>())!;
        var instant = DateTimeOffset.Parse("2026-11-01T01:00:00.1234567Z");
        Assert.Equal(instant, Assert.Single(current.Intervals).StartUtc);
        Assert.Equal(TimeSpan.Zero, current.Intervals[0].StartUtc.Offset);
        using var overlap = await a.PutAsJsonAsync(AvailabilityUrl, new UpdateAvailabilityRequest(current.Version, [Window(), Window()]));
        Assert.Equal(HttpStatusCode.BadRequest, overlap.StatusCode);
        using var offsetless = await a.PutAsJsonAsync(AvailabilityUrl, new { version = current.Version, intervals = new[] { new { startUtc = "2026-11-01T01:00:00", endUtc = "2026-11-01T02:00:00Z" } } });
        Assert.Equal(HttpStatusCode.BadRequest, offsetless.StatusCode);
        using var replace = await a.PutAsJsonAsync(AvailabilityUrl, new UpdateAvailabilityRequest(current.Version, [Window(4), Window(6)]));
        Assert.Equal(HttpStatusCode.OK, replace.StatusCode);
        var replaced = (await replace.Content.ReadFromJsonAsync<AvailabilityResponse>())!;
        using var stale = await a.PutAsJsonAsync(AvailabilityUrl, new UpdateAvailabilityRequest(current.Version, []));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Empty((await Read<AvailabilityResponse>(b, AvailabilityUrl)).Intervals);
        await using var db = app.Context();
        var stored = await db.PatientAvailabilities.AsNoTracking().Where(p => p.ClinicId == app.ClinicA.Id && p.PatientId == patientId).OrderBy(p => p.StartUtc).ToArrayAsync();
        Assert.Equal(new[] { Window(4).StartUtc, Window(6).StartUtc }, stored.Select(p => p.StartUtc));
        Assert.Equal(replaced.Version, (await db.PatientAvailabilitySchedules.SingleAsync(p => p.ClinicId == app.ClinicA.Id)).Version);
        using var clear = await a.PutAsJsonAsync(AvailabilityUrl, new UpdateAvailabilityRequest(replaced.Version, []));
        Assert.Equal(HttpStatusCode.OK, clear.StatusCode);
        var cleared = (await clear.Content.ReadFromJsonAsync<AvailabilityResponse>())!;
        Assert.NotEqual(Guid.Empty, cleared.Version);
        Assert.Empty(cleared.Intervals);
        Assert.Equal(beforeB, await app.SnapshotBAsync());
    }

    [PostgreSqlFact]
    public async Task PostgreSql_enforces_tenant_FKs_uniqueness_checks_and_platform_email_collation()
    {
        await using var app = await PostgreSqlApiFactory.StartAsync();
        var patientId = await app.CreatePatientAsync();
        var beforeB = await app.SnapshotBAsync();
        await using var db = app.Context();
        // Each rejected command uses its own autocommit transaction, so no aborted
        // transaction can mask the next constraint assertion.
        async Task Reject(FormattableString sql, string state)
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            try
            {
                var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(sql));
                Assert.Equal(state, error.SqlState);
            }
            finally { await tx.RollbackAsync(); }
        }
        await Reject($"UPDATE \"Patients\" SET \"UserId\"={app.PatientB.Id} WHERE \"Id\"={patientId} AND \"ClinicId\"={app.ClinicA.Id}", "23503");
        await Reject($"UPDATE \"DoctorProfiles\" SET \"UserId\"={app.DoctorB.Id} WHERE \"Id\"={app.ProfileA.Id} AND \"ClinicId\"={app.ClinicA.Id}", "23503");
        await Reject($"UPDATE \"Users\" SET \"Email\"={app.PatientA.Email} WHERE \"Id\"={app.NewDoctorA.Id} AND \"ClinicId\"={app.ClinicA.Id}", "23505");
        await Reject($"UPDATE \"PlatformAccounts\" SET \"TokenVersion\"={-1} WHERE \"Id\"={app.Platform.Id}", "23514");
        db.DoctorWorkingHours.Add(new() { ClinicId = app.ClinicA.Id, DoctorProfileId = app.ProfileA.Id, DayOfWeek = 1, StartMinute = 540, EndMinute = 780 });
        await db.SaveChangesAsync();
        await Reject($"UPDATE \"DoctorWorkingHours\" SET \"ClinicId\"={app.ClinicB.Id} WHERE \"DoctorProfileId\"={app.ProfileA.Id} AND \"ClinicId\"={app.ClinicA.Id}", "23503");
        await Reject($"UPDATE \"DoctorWorkingHours\" SET \"EndMinute\"={1441} WHERE \"DoctorProfileId\"={app.ProfileA.Id} AND \"ClinicId\"={app.ClinicA.Id}", "23514");
        await Reject($"DELETE FROM \"DoctorProfiles\" WHERE \"Id\"={app.ProfileA.Id} AND \"ClinicId\"={app.ClinicA.Id}", "23001");
        Assert.True(await db.DoctorProfiles.AnyAsync(d => d.Id == app.ProfileA.Id && d.ClinicId == app.ClinicA.Id));
        var upperEmail = app.Platform.Email!.ToUpperInvariant();
        Assert.Equal(app.Platform.Id, (await db.PlatformAccounts.AsNoTracking().SingleAsync(a => a.Email == upperEmail)).Id);
        // Failed insertion rolls back and cannot leave an untracked platform account.
        await Reject($"INSERT INTO \"PlatformAccounts\" (\"FullName\",\"Email\",\"PasswordHash\",\"IsActive\",\"IsEmailVerified\",\"IsPhoneVerified\",\"TokenVersion\",\"CreatedAt\") VALUES ({app.Marker},{upperEmail},{"synthetic-unused"},{false},{false},{false},{0},{DateTime.UtcNow})", "23505");
        var utc = new DateTime(2026, 11, 1, 1, 2, 3, DateTimeKind.Utc).AddTicks(1234560);
        await db.Users.Where(u => u.Id == app.PatientA.Id && u.ClinicId == app.ClinicA.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.CreatedAt, utc));
        var storedUtc = await db.Users.Where(u => u.Id == app.PatientA.Id && u.ClinicId == app.ClinicA.Id).Select(u => u.CreatedAt).SingleAsync();
        Assert.Equal(utc, storedUtc);
        Assert.Equal(DateTimeKind.Utc, storedUtc!.Value.Kind);
        Assert.Equal(beforeB, await app.SnapshotBAsync());
    }

    [PostgreSqlFact]
    public async Task A_real_constraint_failure_rolls_back_all_earlier_writes_in_the_transaction()
    {
        await using var app = await PostgreSqlApiFactory.StartAsync();
        var beforeB = await app.SnapshotBAsync();
        await using (var db = app.Context())
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await db.Services.Where(s => s.Id == app.ServiceA.Id && s.ClinicId == app.ClinicA.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Name, "Must roll back"));
            var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO \"DoctorServices\" (\"ClinicId\",\"DoctorProfileId\",\"ServiceId\",\"DurationMinutes\",\"BufferBeforeMinutes\",\"BufferAfterMinutes\",\"IsActive\") VALUES ({app.ClinicA.Id},{app.ProfileA.Id},{app.ServiceA.Id},{0},{0},{0},{true})"));
            Assert.Equal("23514", error.SqlState);
            await tx.RollbackAsync();
        }
        await using var verify = app.Context();
        Assert.Equal(app.ServiceA.Name, (await verify.Services.SingleAsync(s => s.Id == app.ServiceA.Id && s.ClinicId == app.ClinicA.Id)).Name);
        Assert.Empty(await verify.DoctorServices.Where(s => s.ClinicId == app.ClinicA.Id).ToArrayAsync());
        Assert.Equal(app.ProfileA.Version, (await verify.DoctorProfiles.SingleAsync(d => d.Id == app.ProfileA.Id && d.ClinicId == app.ClinicA.Id)).Version);
        Assert.Equal(beforeB, await app.SnapshotBAsync());
    }
}
