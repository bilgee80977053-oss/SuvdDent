namespace DentalApi.Models;

public enum NotificationChannel { Email, Sms }
public enum NotificationEvent { Confirmed, Cancelled, Reminder }
public enum NotificationState { Pending, Sent, Suppressed, Failed }

// Explicit per-clinic consent. Missing preferences mean no notification consent.
public sealed class NotificationPreference
{
    public int ClinicId { get; set; }
    public int UserId { get; set; }
    public bool EmailEnabled { get; set; }
    public bool SmsEnabled { get; set; }
}

public sealed class NotificationOutbox
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int ClinicId { get; set; }
    public int AppointmentId { get; set; }
    public Appointment Appointment { get; set; } = null!;
    public NotificationEvent Event { get; set; }
    public NotificationChannel Channel { get; set; }
    public string Recipient { get; set; } = "";
    public string Body { get; set; } = "";
    // UTC ticks deliberately use the same sortable representation on both providers.
    public long DueUtcTicks { get; set; }
    public long NextAttemptUtcTicks { get; set; }
    public NotificationState State { get; set; }
    public int Attempts { get; set; }
    public long? SentUtcTicks { get; set; }
}
