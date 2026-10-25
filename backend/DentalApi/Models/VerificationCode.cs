
namespace DentalApi.Models;

public class VerificationCode
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public User User { get; set; } = null!;

    // email эсвэл sms
    public string Channel { get; set; } = "";

    // registration эсвэл password-reset
    public string Purpose { get; set; } = "registration";

    public string CodeHash { get; set; } = "";

    public DateTime ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsUsed { get; set; } = false;

    public int FailedAttempts { get; set; } = 0;
}
