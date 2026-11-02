namespace DentalApi.Models;

public enum PhoneVerificationDeliveryState { Pending, Accepted, Failed }

public sealed class PhoneVerificationChallenge
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int ClinicId { get; set; }
    public int UserId { get; set; }
    public string PhoneSnapshot { get; set; } = "";
    public string Destination { get; set; } = "";
    public string CodeHash { get; set; } = "";
    public long CreatedUtcTicks { get; set; }
    public long ExpiresUtcTicks { get; set; }
    public int FailedAttempts { get; set; }
    public long? LastFailedUtcTicks { get; set; }
    public PhoneVerificationDeliveryState DeliveryState { get; set; }
    public bool IsConsumed { get; set; }
    public bool IsInvalidated { get; set; }
}
