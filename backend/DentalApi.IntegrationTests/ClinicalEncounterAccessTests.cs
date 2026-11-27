using System.Security.Claims;
using DentalApi.Data;
using DentalApi.Models;
using DentalApi.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DentalApi.IntegrationTests;

public sealed class ClinicalEncounterAccessTests
{
    [Fact]
    public async Task Only_the_active_assigned_doctor_can_open_a_started_confirmed_encounter()
    {
        using var app = new DentalApiFactory();
        await app.SeedAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var access = scope.ServiceProvider.GetRequiredService<ClinicalEncounterAccess>();
        var assigned = new DoctorProfile { ClinicId = 101, UserId = 13, IsActive = true };
        var service = new Service { ClinicId = 101, Name = "Synthetic clinical service" };
        db.DoctorProfiles.Add(assigned);
        db.Services.Add(service);
        db.Users.Add(new User { Id = 115, ClinicId = 101, Role = Role.Doctor, FullName = "Other synthetic doctor" });
        await db.SaveChangesAsync();
        db.DoctorServices.Add(new DoctorService
        {
            ClinicId = 101, DoctorProfileId = assigned.Id, ServiceId = service.Id, DurationMinutes = 30
        });
        await db.SaveChangesAsync();

        var now = DateTimeOffset.UtcNow;
        var eligible = AppointmentAt(now.AddHours(-4));
        var cancelled = AppointmentAt(now.AddHours(-2), AppointmentStatus.Cancelled);
        var future = AppointmentAt(now.AddDays(1));
        db.Appointments.AddRange(eligible, cancelled, future);
        await db.SaveChangesAsync();
        var clinician = Principal(13, 101, Role.Doctor);
        Assert.Equal(new ClinicalEncounterScope(101, 21, eligible.Id, 13),
            await access.FindAsync(clinician, eligible.Id));
        Assert.Null(await access.FindAsync(clinician, cancelled.Id));
        Assert.Null(await access.FindAsync(clinician, future.Id));
        Assert.Null(await access.FindAsync(clinician, int.MaxValue));
        Assert.Null(await access.FindAsync(Principal(115, 101, Role.Doctor), eligible.Id));
        Assert.Null(await access.FindAsync(Principal(13, 202, Role.Doctor), eligible.Id));
        Assert.Null(await access.FindAsync(Principal(11, 101, Role.Doctor), eligible.Id));
        Assert.Null(await access.FindAsync(new ClaimsPrincipal(new ClaimsIdentity()), eligible.Id));
        foreach (var excluded in Enum.GetValues<Role>().Where(role => role != Role.Doctor))
            Assert.Null(await access.FindAsync(Principal(13, 101, excluded), eligible.Id));

        var user = (await db.Users.FindAsync(13))!;
        user.IsActive = false;
        await db.SaveChangesAsync();
        Assert.Null(await access.FindAsync(clinician, eligible.Id));
        user.IsActive = true;
        user.Role = Role.Director;
        await db.SaveChangesAsync();
        Assert.Null(await access.FindAsync(clinician, eligible.Id));
        user.Role = Role.Doctor;
        assigned.IsActive = false;
        await db.SaveChangesAsync();
        Assert.Null(await access.FindAsync(clinician, eligible.Id));
        assigned.IsActive = true;
        (await db.Patients.FindAsync(21))!.IsActive = false;
        await db.SaveChangesAsync();
        Assert.Null(await access.FindAsync(clinician, eligible.Id));

        Appointment AppointmentAt(DateTimeOffset start, AppointmentStatus status = AppointmentStatus.Confirmed)
            => new()
            {
                ClinicId = 101, PatientId = 21, DoctorProfileId = assigned.Id, ServiceId = service.Id,
                StartUtc = start, EndUtc = start.AddMinutes(30),
                OccupiedStartUtc = start, OccupiedEndUtc = start.AddMinutes(30),
                DurationMinutesSnapshot = 30, ClinicTimeZoneIdSnapshot = "Asia/Ulaanbaatar",
                Status = status, RequestId = Guid.NewGuid()
            };
    }

    private static ClaimsPrincipal Principal(int userId, int clinicId, Role role) =>
        new(new ClaimsIdentity(
        [
            new Claim("userId", userId.ToString()),
            new Claim("clinicId", clinicId.ToString()),
            new Claim(ClaimTypes.Role, role.ToString())
        ], "SyntheticClinic"));
}
