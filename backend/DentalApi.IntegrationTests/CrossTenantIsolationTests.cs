using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DentalApi.Data;
using DentalApi.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DentalApi.IntegrationTests;

public sealed class CrossTenantIsolationTests
{
    [Fact]
    public async Task Login_catalog_appointment_and_admin_boundaries_preserve_the_other_clinic()
    {
        using var app = new DentalApiFactory();
        await app.SeedAsync();
        var ids = await SeedTenantRecordsAsync(app);
        var clinicOneBefore = await SnapshotClinicAsync(app, 101);

        using var anonymous = app.CreateTestClient();
        using (var wrongClinic = await anonymous.PostAsJsonAsync("/api/Auth/login", new
        {
            identifier = "user12@example.invalid",
            password = DentalApiFactory.Password,
            clinicId = 101
        }))
            Assert.Equal(HttpStatusCode.Unauthorized, wrongClinic.StatusCode);

        using var publicClinicOne = await anonymous.GetAsync("/api/Services?clinicId=101");
        using var publicClinicTwo = await anonymous.GetAsync("/api/Services?clinicId=202");
        Assert.Equal(HttpStatusCode.OK, publicClinicOne.StatusCode);
        Assert.Equal(HttpStatusCode.OK, publicClinicTwo.StatusCode);
        Assert.Equal(ids.ClinicOneServiceId, await SingleIdAsync(publicClinicOne));
        Assert.Equal(ids.ClinicTwoServiceId, await SingleIdAsync(publicClinicTwo));
        using var anonymousPrivateCatalog = await anonymous.GetAsync("/api/appointments/services");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousPrivateCatalog.StatusCode);

        using var clinicTwoPatient = await LoginAsync(app, 12, 202);
        using var privateCatalog = await clinicTwoPatient.GetAsync("/api/appointments/services?clinicId=101");
        Assert.Equal(HttpStatusCode.OK, privateCatalog.StatusCode);
        Assert.Equal(ids.ClinicTwoServiceId, await SingleIdAsync(privateCatalog));
        var clinicTwoDoctors = await clinicTwoPatient.GetFromJsonAsync<JsonElement[]>("/api/Doctors");
        Assert.Equal(ids.ClinicTwoDoctorProfileId,
            Assert.Single(clinicTwoDoctors!).GetProperty("id").GetInt32());

        using var foreignAppointment = await clinicTwoPatient.GetAsync($"/api/appointments/{ids.ClinicOneAppointmentId}");
        Assert.Equal(HttpStatusCode.NotFound, foreignAppointment.StatusCode);
        var ownAppointments = await clinicTwoPatient.GetFromJsonAsync<JsonElement>("/api/appointments/me");
        Assert.Empty(ownAppointments.GetProperty("items").EnumerateArray());
        using var clinicOnePatient = await LoginAsync(app, 11, 101);
        using var ownAppointment = await clinicOnePatient.GetAsync($"/api/appointments/{ids.ClinicOneAppointmentId}");
        Assert.Equal(HttpStatusCode.OK, ownAppointment.StatusCode);

        using (var patientAdminRead = await clinicTwoPatient.GetAsync("/api/admin/services"))
            Assert.Equal(HttpStatusCode.Forbidden, patientAdminRead.StatusCode);
        using (var patientAdminPing = await clinicTwoPatient.GetAsync("/api/Admin/ping"))
            Assert.Equal(HttpStatusCode.Forbidden, patientAdminPing.StatusCode);

        using var clinicTwoDirector = await LoginAsync(app, 16, 202);
        using (var foreignService = await clinicTwoDirector.GetAsync($"/api/admin/services/{ids.ClinicOneServiceId}"))
            Assert.Equal(HttpStatusCode.NotFound, foreignService.StatusCode);
        using (var foreignServiceUpdate = await clinicTwoDirector.PutAsJsonAsync(
                   $"/api/admin/services/{ids.ClinicOneServiceId}",
                   new { name = "Synthetic changed", description = "Synthetic only", minPrice = 1, maxPrice = 1 }))
            Assert.Equal(HttpStatusCode.NotFound, foreignServiceUpdate.StatusCode);
        using (var foreignDoctor = await clinicTwoDirector.GetAsync($"/api/admin/Doctors/{ids.ClinicOneDoctorProfileId}"))
            Assert.Equal(HttpStatusCode.NotFound, foreignDoctor.StatusCode);

        Assert.Equal(clinicOneBefore, await SnapshotClinicAsync(app, 101));
    }

    [Fact]
    public async Task Director_service_writes_require_an_explicit_minimum_tariff_but_allow_zero()
    {
        using var app = new DentalApiFactory();
        await app.SeedAsync();
        var ids = await SeedTenantRecordsAsync(app);
        using var director = await LoginAsync(app, 16, 202);
        const string catalog = "/api/admin/services";
        var existing = $"{catalog}/{ids.ClinicTwoServiceId}";

        var incomplete = new { name = "Synthetic rejected", description = "Synthetic only" };
        using var createWithoutPrice = await director.PostAsJsonAsync(catalog, incomplete);
        using var updateWithoutPrice = await director.PutAsJsonAsync(existing, incomplete);
        Assert.Equal(HttpStatusCode.BadRequest, createWithoutPrice.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, updateWithoutPrice.StatusCode);

        var services = await director.GetFromJsonAsync<JsonElement[]>(catalog);
        var original = Assert.Single(services!);
        Assert.Equal(ids.ClinicTwoServiceId, original.GetProperty("id").GetInt32());
        Assert.Equal("Synthetic clinic two service", original.GetProperty("name").GetString());
        Assert.Equal(20, original.GetProperty("minPrice").GetInt64());

        var explicitZero = new { name = "Synthetic zero tariff", description = "Synthetic only", minPrice = 0 };
        using var createZero = await director.PostAsJsonAsync(catalog, explicitZero);
        Assert.Equal(HttpStatusCode.Created, createZero.StatusCode);
        var created = await createZero.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, created.GetProperty("minPrice").GetInt64());
        using var updateZero = await director.PutAsJsonAsync(existing, explicitZero with { name = "Synthetic updated tariff" });
        Assert.Equal(HttpStatusCode.OK, updateZero.StatusCode);
        var updated = await updateZero.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, updated.GetProperty("minPrice").GetInt64());
    }

    private static async Task<(int ClinicOneServiceId, int ClinicTwoServiceId,
        int ClinicOneDoctorProfileId, int ClinicTwoDoctorProfileId, int ClinicOneAppointmentId)>
        SeedTenantRecordsAsync(DentalApiFactory app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hash = BCrypt.Net.BCrypt.HashPassword(DentalApiFactory.Password, workFactor: 4);
        var directorOne = NewUser(15, 101, "Synthetic director one", Role.Director, hash);
        var directorTwo = NewUser(16, 202, "Synthetic director two", Role.Director, hash);
        var doctorTwo = NewUser(17, 202, "Synthetic doctor two", Role.Doctor, hash);
        db.Users.AddRange(directorOne, directorTwo, doctorTwo);

        var serviceOne = new Service
        {
            ClinicId = 101, Name = "Synthetic clinic one service",
            Description = "Synthetic catalog row.", MinPrice = 10, MaxPrice = 10
        };
        var serviceTwo = new Service
        {
            ClinicId = 202, Name = "Synthetic clinic two service",
            Description = "Synthetic catalog row.", MinPrice = 20, MaxPrice = 20
        };
        db.Services.AddRange(serviceOne, serviceTwo);
        var doctorOne = new DoctorProfile
        {
            ClinicId = 101, UserId = 13, Specialty = "Synthetic", PracticeStartYear = 2020,
            IsPublished = true, IsActive = true
        };
        var doctorProfileTwo = new DoctorProfile
        {
            ClinicId = 202, UserId = doctorTwo.Id, Specialty = "Synthetic", PracticeStartYear = 2020,
            IsPublished = true, IsActive = true
        };
        db.DoctorProfiles.AddRange(doctorOne, doctorProfileTwo);
        await db.SaveChangesAsync();
        db.DoctorServices.AddRange(
            new DoctorService
            {
                ClinicId = 101, DoctorProfileId = doctorOne.Id, ServiceId = serviceOne.Id,
                DurationMinutes = 30, BufferBeforeMinutes = 0, BufferAfterMinutes = 0
            },
            new DoctorService
            {
                ClinicId = 202, DoctorProfileId = doctorProfileTwo.Id, ServiceId = serviceTwo.Id,
                DurationMinutes = 30, BufferBeforeMinutes = 0, BufferAfterMinutes = 0
            });
        await db.SaveChangesAsync();

        var start = DateTimeOffset.UtcNow.AddDays(1);
        var appointment = new Appointment
        {
            ClinicId = 101,
            PatientId = 21,
            DoctorProfileId = doctorOne.Id,
            ServiceId = serviceOne.Id,
            StartUtc = start,
            EndUtc = start.AddMinutes(30),
            OccupiedStartUtc = start,
            OccupiedEndUtc = start.AddMinutes(30),
            DurationMinutesSnapshot = 30,
            BufferBeforeMinutesSnapshot = 0,
            BufferAfterMinutesSnapshot = 0,
            ClinicTimeZoneIdSnapshot = "Asia/Ulaanbaatar",
            RequestId = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();
        return (serviceOne.Id, serviceTwo.Id, doctorOne.Id, doctorProfileTwo.Id, appointment.Id);
    }

    private static User NewUser(int id, int clinicId, string name, Role role, string hash) => new()
    {
        Id = id,
        ClinicId = clinicId,
        FullName = name,
        Email = $"user{id}@example.invalid",
        PasswordHash = hash,
        Role = role,
        IsActive = true,
        IsEmailVerified = true
    };

    private static async Task<HttpClient> LoginAsync(DentalApiFactory app, int userId, int clinicId)
    {
        var client = app.CreateTestClient();
        using var response = await client.PostAsJsonAsync("/api/Auth/login", new
        {
            identifier = $"user{userId}@example.invalid",
            password = DentalApiFactory.Password,
            clinicId
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body.GetProperty("token").GetString());
        return client;
    }

    private static async Task<int> SingleIdAsync(HttpResponseMessage response)
    {
        var rows = await response.Content.ReadFromJsonAsync<JsonElement[]>();
        return Assert.Single(rows!).GetProperty("id").GetInt32();
    }

    private static async Task<string> SnapshotClinicAsync(DentalApiFactory app, int clinicId)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return JsonSerializer.Serialize(new
        {
            Clinics = await db.Clinics.AsNoTracking().Where(row => row.Id == clinicId).ToArrayAsync(),
            Users = await db.Users.AsNoTracking().Where(row => row.ClinicId == clinicId)
                .OrderBy(row => row.Id).ToArrayAsync(),
            Patients = await db.Patients.AsNoTracking().Where(row => row.ClinicId == clinicId)
                .OrderBy(row => row.Id).ToArrayAsync(),
            Services = await db.Services.AsNoTracking().Where(row => row.ClinicId == clinicId)
                .OrderBy(row => row.Id).ToArrayAsync(),
            Profiles = await db.DoctorProfiles.AsNoTracking().Where(row => row.ClinicId == clinicId)
                .OrderBy(row => row.Id).ToArrayAsync(),
            Appointments = await db.Appointments.AsNoTracking().Where(row => row.ClinicId == clinicId)
                .OrderBy(row => row.Id).ToArrayAsync()
        });
    }
}
