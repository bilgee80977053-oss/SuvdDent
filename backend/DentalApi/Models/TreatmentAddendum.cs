namespace DentalApi.Models;

internal sealed class TreatmentAddendum
{
    public int ClinicId { get; init; }
    public int AppointmentId { get; init; }
    public Guid RequestId { get; init; }
    public int AuthorUserId { get; init; }
    public string Note { get; init; } = "";
    public DateTimeOffset SignedUtc { get; init; }
}
