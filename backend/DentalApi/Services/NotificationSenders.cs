using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace DentalApi.Services;

public interface INotificationEmailSender
{
    Task SendAsync(string recipient, string body, string deliveryKey, CancellationToken cancellationToken);
}
public interface ISmsSender
{
    Task SendAsync(string recipient, string body, string deliveryKey, CancellationToken cancellationToken);
}

public sealed class NotificationEmailSender(EmailService email, IConfiguration config) : INotificationEmailSender
{
    public Task SendAsync(string recipient, string body, string deliveryKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!config.GetValue<bool>("AppointmentEmail:Enabled"))
            throw new InvalidOperationException("Appointment email transport is disabled.");
        return email.SendAsync(recipient, "Nexora Dental - Захиалгын мэдэгдэл", body,
            deliveryKey + "@notifications.nexora.invalid", cancellationToken);
    }
}

// Gateway contract: HTTPS POST { recipient, message }, Idempotency-Key header,
// 2xx means durably accepted. The configured gateway must deduplicate this key.
public sealed class GatewaySmsSender(HttpClient client, IConfiguration config) : ISmsSender
{
    public async Task SendAsync(string recipient, string body, string deliveryKey, CancellationToken cancellationToken)
    {
        if (!config.GetValue<bool>("Notifications:Sms:Enabled") ||
            !Uri.TryCreate(config["Notifications:Sms:Endpoint"], UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("SMS gateway is disabled or not configured.");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Add("Idempotency-Key", deliveryKey);
        var token = config["Notifications:Sms:Token"];
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { recipient, message = body });
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
