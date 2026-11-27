namespace DentalApi.Models;

internal enum ClinicalAuditAction { HistoryRead, TreatmentRecorded, TreatmentAmended }

// Keep the record metadata-only. The same-clinic appointment identifies the
// patient; do not store another patient identifier or any clinical payload here.
internal sealed class ClinicalAuditEvent
{
    public long Id { get; set; }
    public int ClinicId { get; init; }
    public int AppointmentId { get; init; }
    public int ActorUserId { get; init; }
    public ClinicalAuditAction Action { get; init; }
    public DateTimeOffset OccurredUtc { get; init; }
    public Guid CorrelationId { get; init; }
}
