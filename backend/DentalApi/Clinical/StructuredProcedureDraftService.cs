using System.Security.Claims;
using DentalApi.Data;
using DentalApi.Services;
using Microsoft.EntityFrameworkCore;

namespace DentalApi.Clinical;

internal sealed record StructuredProcedureDraftInput(
    int AppointmentId, int ServiceId, StructuredTreatmentScope Scope, string? ToothCode);

internal enum StructuredProcedureDraftStatus { Invalid, Unavailable, Validated }

// A server-derived, nonfinancial draft. It is not an executable authorization,
// catalog revision, signed clinical record, approved quote, or seller snapshot.
internal sealed class StructuredProcedureSnapshot(
    int clinicId, int patientId, int appointmentId, int doctorUserId,
    int serviceId, string serviceLabel, StructuredTreatmentTarget target)
{
    internal int ClinicId { get; } = clinicId;
    internal int PatientId { get; } = patientId;
    internal int AppointmentId { get; } = appointmentId;
    internal int DoctorUserId { get; } = doctorUserId;
    internal int ServiceId { get; } = serviceId;
    internal string ServiceLabel { get; } = serviceLabel;
    internal StructuredTreatmentTarget Target { get; } = target;
}

internal sealed record StructuredProcedureDraftResult(
    StructuredProcedureDraftStatus Status, StructuredProcedureSnapshot? Snapshot = null);

internal sealed class StructuredProcedureDraftService(AppDbContext db, ClinicalEncounterAccess encounterAccess)
{
    // Internal callers must supply a principal already validated by clinic authentication.
    internal async Task<StructuredProcedureDraftResult> ValidateAsync(
        ClaimsPrincipal actor, StructuredProcedureDraftInput input, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (input.AppointmentId <= 0 || input.ServiceId <= 0 ||
            !StructuredTreatmentTarget.TryCreate(input.Scope, input.ToothCode, out var target))
            return new(StructuredProcedureDraftStatus.Invalid);

        var encounter = await encounterAccess.FindAsync(actor, input.AppointmentId, cancellationToken);
        if (encounter is null)
            return new(StructuredProcedureDraftStatus.Unavailable);

        // Select only nonfinancial fields from the actual tenant catalog.
        var service = await db.Services.AsNoTracking()
            .Where(row => row.ClinicId == encounter.ClinicId && row.Id == input.ServiceId && row.IsActive)
            .Select(row => new { row.Id, row.Name })
            .SingleOrDefaultAsync(cancellationToken);
        if (service is null || string.IsNullOrWhiteSpace(service.Name) ||
            service.Name.Length is < 2 or > 160 || service.Name.Any(char.IsControl))
            return new(StructuredProcedureDraftStatus.Unavailable);

        return new(StructuredProcedureDraftStatus.Validated, new(
            encounter.ClinicId, encounter.PatientId, encounter.AppointmentId, encounter.DoctorUserId,
            service.Id, service.Name, target));
    }
}
