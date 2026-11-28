using System.Security.Claims;
using DentalApi.Data;
using DentalApi.Models;
using Microsoft.EntityFrameworkCore;

namespace DentalApi.Services;

internal sealed record ClinicalEncounterScope(int ClinicId, int PatientId, int AppointmentId, int DoctorUserId);

// Internal first-release boundary, not a clinical-data endpoint. Future entry points
// must use validated clinic authentication and couple this check to durable audit.
internal sealed class ClinicalEncounterAccess(AppDbContext db, TimeProvider clock)
{
    internal async Task<ClinicalEncounterScope?> FindAsync(
        ClaimsPrincipal principal, int appointmentId, CancellationToken cancellationToken = default)
    {
        if (principal.Identity?.IsAuthenticated != true ||
            !principal.IsInRole(nameof(Role.Doctor)) ||
            !int.TryParse(principal.FindFirstValue("clinicId"), out var clinicId) || clinicId <= 0 ||
            !int.TryParse(principal.FindFirstValue("userId"), out var userId) || userId <= 0 ||
            appointmentId <= 0)
            return null;

        return await AssignedEncounters(clinicId, userId)
            .Where(row => row.Id == appointmentId)
            .Select(row => new ClinicalEncounterScope(row.ClinicId, row.PatientId, row.Id, userId))
            .SingleOrDefaultAsync(cancellationToken);
    }

    // Query only with clinic/user identities obtained from validated clinic claims
    // or from a fresh FindAsync result in the same transaction.
    internal IQueryable<Appointment> AssignedEncounters(int clinicId, int userId)
    {
        var now = clock.GetUtcNow();
        return
            from appointment in db.Appointments.AsNoTracking()
            join patient in db.Patients
                on new { appointment.ClinicId, appointment.PatientId }
                equals new { patient.ClinicId, PatientId = patient.Id }
            join doctor in db.DoctorProfiles
                on new { appointment.ClinicId, appointment.DoctorProfileId }
                equals new { doctor.ClinicId, DoctorProfileId = doctor.Id }
            join user in db.Users
                on new { doctor.ClinicId, doctor.UserId }
                equals new { user.ClinicId, UserId = user.Id }
            where appointment.ClinicId == clinicId &&
                  appointment.Status == AppointmentStatus.Confirmed && appointment.StartUtc <= now &&
                  patient.IsActive && doctor.IsActive && user.Id == userId &&
                  user.IsActive && user.Role == Role.Doctor
            select appointment;
    }
}
