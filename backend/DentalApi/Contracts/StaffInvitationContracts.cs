using System.ComponentModel.DataAnnotations;

namespace DentalApi.Contracts;

// Director can invite a Doctor account only; a caller cannot choose a role.
public record InviteDoctorRequest(
    [Required, StringLength(200, MinimumLength = 2)] string FullName,
    [Required, EmailAddress, StringLength(254)] string Email);

public record AcceptDoctorInvitationRequest(
    [Range(1, int.MaxValue)] int ClinicId,
    [Required, EmailAddress, StringLength(254)] string Email,
    [Required, StringLength(8, MinimumLength = 8)] string Code,
    [Required, StringLength(128, MinimumLength = 12)] string NewPassword);

public record DirectorStaffResponse(int UserId, string FullName, string Email,
    string Role, bool IsActive, bool IsEmailVerified);
