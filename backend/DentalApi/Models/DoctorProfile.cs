namespace DentalApi.Models;

public class DoctorProfile
{
    public int Id { get; set; }
    public int ClinicId { get; set; }
    public int UserId { get; set; }
    public string Specialty { get; set; } = "";
    public int PracticeStartYear { get; set; }
    public string? Biography { get; set; }
    public string? PhotoPath { get; set; }
    public bool IsPublished { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public Guid Version { get; set; } = Guid.NewGuid();
    public User User { get; set; } = null!;
    public ICollection<DoctorWorkingHours> WorkingHours { get; set; } = new List<DoctorWorkingHours>();
}