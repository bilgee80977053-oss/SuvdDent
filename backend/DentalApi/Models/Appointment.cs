namespace DentalApi.Models;

public enum AppointmentStatus { Confirmed, Cancelled }

public class Appointment
{
    public int Id { get; set; }
    public int ClinicId { get; set; }
    public int DoctorProfileId { get; set; }
    public int ServiceId { get; set; }
    public int PatientId { get; set; }
    public int PlatformAccountId { get; set; }
    public AppointmentStatus Status { get; set; } = AppointmentStatus.Confirmed;
    public DateTimeOffset StartUtc { get; set; }
    public DateTimeOffset EndUtc { get; set; }
    public DateTimeOffset OccupiedStartUtc { get; set; }
    public DateTimeOffset OccupiedEndUtc { get; set; }
    public int DurationMinutesSnapshot { get; set; }
    public int BufferBeforeMinutesSnapshot { get; set; }
    public int BufferAfterMinutesSnapshot { get; set; }
    public string TimeZoneIdSnapshot { get; set; } = "";
    public string DoctorNameSnapshot { get; set; } = "";
    public string ServiceNameSnapshot { get; set; } = "";
    public long MinPriceSnapshot { get; set; }
    public long? MaxPriceSnapshot { get; set; }
    public Guid ClinicVersionSnapshot { get; set; }
    public Guid DoctorVersionSnapshot { get; set; }
    public Guid IdempotencyKey { get; set; }
    public string RequestHash { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? PatientCreationConsentAtUtc { get; set; }
    public string? PatientCreationConsentVersion { get; set; }
}
