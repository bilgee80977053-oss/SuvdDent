namespace DentalApi.Models;

// Weekly local wall-clock intervals. This is not appointment availability.
public class DoctorWorkingHours
{
    public int Id { get; set; }
    public int ClinicId { get; set; }
    public int DoctorProfileId { get; set; }
    public int DayOfWeek { get; set; } // Sunday = 0, Saturday = 6.
    public int StartMinute { get; set; }
    public int EndMinute { get; set; }
}