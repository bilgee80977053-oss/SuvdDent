namespace DentalApi.Models;

// The clinic-scoped appointment is the source of patient, doctor profile,
// and booked service identity. The signed note never duplicates those fields.
internal sealed class TreatmentRecord
{
    public int ClinicId { get; init; }
    public int AppointmentId { get; init; }
    public int AuthorUserId { get; init; }
    public string Note { get; init; } = "";
    public DateTimeOffset SignedUtc { get; init; }
}
