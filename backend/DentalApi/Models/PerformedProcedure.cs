namespace DentalApi.Models;

internal sealed class PerformedProcedure
{
    public int ClinicId { get; init; }
    public int AppointmentId { get; init; }
    public Guid RequestId { get; init; }
    public int DoctorUserId { get; init; }
    public int ServiceId { get; init; }
    public string ServiceLabel { get; init; } = "";
    public string TargetScope { get; init; } = "";
    public string? ToothCode { get; init; }
    public DateTimeOffset SignedUtc { get; init; }
}
