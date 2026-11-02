using System.Globalization;

namespace DentalApi.Services;

public sealed record AppointmentEmailNotification(
    int AppointmentId,
    string Recipient,
    string Clinic,
    string Service,
    string Doctor,
    DateTimeOffset LocalStart,
    string TimeZoneId);

public interface IAppointmentEmailSender
{
    Task SendConfirmedAsync(AppointmentEmailNotification notification);
}

// Deliberate opt-in: test fixtures and local rehearsals never send real mail.
public sealed class SmtpAppointmentEmailSender(EmailService email, IConfiguration config) : IAppointmentEmailSender
{
    public Task SendConfirmedAsync(AppointmentEmailNotification notification)
    {
        if (!string.Equals(config["AppointmentEmail:Enabled"], "true", StringComparison.OrdinalIgnoreCase))
            return Task.CompletedTask;

        var localTime = notification.LocalStart.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        var body = $"Таны цагийн захиалга баталгаажлаа.\n\n" +
            $"Эмнэлэг: {notification.Clinic}\n" +
            $"Үйлчилгээ: {notification.Service}\n" +
            $"Эмч: {notification.Doctor}\n" +
            $"Цаг: {localTime} ({notification.TimeZoneId})\n" +
            $"Захиалгын дугаар: {notification.AppointmentId}\n\n" +
            "Энэ нь автомат мэдэгдэл юм.";

        return email.SendAsync(notification.Recipient, "Nexora Dental - Цагийн захиалга баталгаажлаа", body);
    }
}
