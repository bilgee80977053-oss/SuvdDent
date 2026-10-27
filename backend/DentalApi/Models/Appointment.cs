namespace DentalApi.Models;

public enum AppointmentStatus { Confirmed, Cancelled }

// Persistence foundation only. Booking authorization, working-hour validation,
// overlap prevention and confirmation email delivery belong to later services.
// Treatment and occupied times are half-open intervals: [start, end).
public class Appointment
{
    public int Id { get; set; }
    public int ClinicId { get; set; }
    public int PatientId { get; set; }
    public int DoctorProfileId { get; set; }
    public int ServiceId { get; set; }
    public DateTimeOffset StartUtc { get; set; }
    public DateTimeOffset EndUtc { get; set; }
    public DateTimeOffset OccupiedStartUtc { get; set; }
    public DateTimeOffset OccupiedEndUtc { get; set; }
    public int DurationMinutesSnapshot { get; set; }
    public int BufferBeforeMinutesSnapshot { get; set; }
    public int BufferAfterMinutesSnapshot { get; set; }
    // Required by the existing clinic scheduling policy's historical display contract.
    public string ClinicTimeZoneIdSnapshot { get; set; } = "";
    public AppointmentStatus Status { get; set; } = AppointmentStatus.Confirmed;
    public Guid RequestId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
