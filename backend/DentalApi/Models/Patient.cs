
namespace DentalApi.Models;

public class Patient
{
    public int Id { get; set; }

    public int ClinicId { get; set; }

    public int? UserId { get; set; }

    public string FullName { get; set; } = "";

    public string Phone { get; set; } = "";

    public DateTime? DateOfBirth { get; set; }

    public string? Address { get; set; }

    public string? Allergies { get; set; }

    public string? MedicalNotes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsActive { get; set; } = true;
}
