namespace DentalApi.Models;

// An immutable record of a committed move. The replacement stores its own booking
// key; the client retry key is scoped to this source appointment and clinic.
public sealed class AppointmentReschedule
{
    public int ClinicId { get; set; }
    public int SourceId { get; set; }
    public int ReplacementId { get; set; }
    public Guid RequestId { get; set; }
}
