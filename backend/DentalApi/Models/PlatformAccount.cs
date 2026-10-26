namespace DentalApi.Models;

public class PlatformAccount
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string PasswordHash { get; set; } = "";
    public bool IsActive { get; set; }
    public bool IsEmailVerified { get; set; }
    public bool IsPhoneVerified { get; set; }
    public int TokenVersion { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class PlatformVerificationCode
{
    public int Id { get; set; }
    public int PlatformAccountId { get; set; }
    public string Channel { get; set; } = "";
    public string Purpose { get; set; } = "";
    public string CodeHash { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public int FailedAttempts { get; set; }
    public bool IsUsed { get; set; }
}

// No public auto-link endpoint: possession of a contact does not prove card ownership.
public class ClinicPatientLink
{
    public int Id { get; set; }
    public int PlatformAccountId { get; set; }
    public int ClinicId { get; set; }
    public int PatientId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
