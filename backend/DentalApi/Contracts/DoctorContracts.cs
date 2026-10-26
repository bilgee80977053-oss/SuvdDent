using System.ComponentModel.DataAnnotations;

namespace DentalApi.Contracts;

public record DoctorDetails(
    [Required, StringLength(200)] string Specialty,
    [Range(1900, 2100)] int PracticeStartYear,
    [StringLength(4000)] string? Biography,
    [StringLength(300)] string? PhotoPath);

public record CreateDoctorRequest(
    [Range(1, int.MaxValue)] int UserId,
    [Required] DoctorDetails Profile,
    bool IsPublished = false);

public record UpdateDoctorRequest(Guid Version, [Required] DoctorDetails Profile);
public record ManageDoctorRequest(Guid Version, [Required] DoctorDetails Profile,
    bool IsPublished, bool IsActive);

public record WorkingHoursInput(
    [Range(0, 6)] int DayOfWeek,
    [Range(0, 1439)] int StartMinute,
    [Range(1, 1440)] int EndMinute);

public record UpdateWorkingHoursRequest(Guid Version,
    [Required, MaxLength(70)] WorkingHoursInput[] Intervals);

public record DoctorResponse(int Id, string FullName, string Specialty,
    int PracticeStartYear, string? Biography, string? PhotoPath,
    bool IsPublished, bool IsActive, Guid Version, WorkingHoursInput[] WorkingHours);