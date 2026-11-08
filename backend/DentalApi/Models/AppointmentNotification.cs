namespace DentalApi.Models;

// Contains DEMO content only, never contact addresses or medical records.
public sealed class AppointmentNotification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int AppointmentId { get; set; }
    public int ClinicId { get; set; }
    public int PlatformAccountId { get; set; }
    public string Channel { get; set; } = "";
    public string Mode { get; set; } = "Demo";
    public string TemplateVersion { get; set; } = "appointment-demo-v1";
    public string? Subject { get; set; }
    public string? Body { get; set; }
    public string Status { get; set; } = "Pending";
    public int AttemptCount { get; set; }
    public DateTime? NextAttemptAtUtc { get; set; }
    public Guid? LeaseToken { get; set; }
    public DateTime? LeaseExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
    public string? LastErrorCode { get; set; }
}
