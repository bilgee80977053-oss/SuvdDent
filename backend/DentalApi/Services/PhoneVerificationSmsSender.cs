namespace DentalApi.Services;

public interface IPhoneVerificationSmsSender
{
    bool IsAvailable { get; }
    Task SendAsync(string destination, string code, string deliveryKey, CancellationToken ct);
}

public sealed class PhoneVerificationSmsSender(ISmsSender sender, IConfiguration config) : IPhoneVerificationSmsSender
{
    public bool IsAvailable => config.GetValue<bool>("Notifications:Sms:Enabled") &&
        Uri.TryCreate(config["Notifications:Sms:Endpoint"], UriKind.Absolute, out var endpoint) &&
        endpoint.Scheme == Uri.UriSchemeHttps;

    public async Task SendAsync(string destination, string code, string deliveryKey, CancellationToken ct)
    {
        if (!IsAvailable) throw new InvalidOperationException("SMS delivery is unavailable.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        // Plaintext exists only in memory and the outbound provider request.
        await sender.SendAsync(destination,
            "Nexora Dental - Утас баталгаажуулах код: " + code + ". Код 120 секунд хүчинтэй.",
            deliveryKey, timeout.Token);
    }
}
