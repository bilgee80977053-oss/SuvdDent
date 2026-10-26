namespace DentalApi.Models;

public class DoctorService
{
    public int Id { get; set; }
    public int ClinicId { get; set; }
    public int DoctorProfileId { get; set; }
    public int ServiceId { get; set; }
    public int DurationMinutes { get; set; }
    public int BufferBeforeMinutes { get; set; }
    public int BufferAfterMinutes { get; set; }
    public bool IsActive { get; set; } = true;
}

// Retained even for an empty list, preventing stale writes after clearing availability.
public class PatientAvailabilitySchedule
{
    public int ClinicId { get; set; }
    public int PatientId { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}

public class PatientAvailability
{
    public int Id { get; set; }
    public int ClinicId { get; set; }
    public int PatientId { get; set; }
    public DateTimeOffset StartUtc { get; set; }
    public DateTimeOffset EndUtc { get; set; }
}
