using System.Collections.Concurrent;
using DentalApi.Services;

namespace DentalApi.IntegrationTests;

public sealed record SentNotification(string Recipient, string Body, string Key);

public sealed class FakeSmsSender : ISmsSender
{
    public ConcurrentQueue<SentNotification> Sent { get; } = new();
    public ConcurrentQueue<string> AttemptKeys { get; } = new();
    public bool Fail { get; set; }
    public Task SendAsync(string recipient, string body, string deliveryKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AttemptKeys.Enqueue(deliveryKey);
        if (Fail) throw new InvalidOperationException("Synthetic SMS failure");
        Sent.Enqueue(new(recipient, body, deliveryKey));
        return Task.CompletedTask;
    }
}

public sealed class FakeNotificationEmailSender : INotificationEmailSender
{
    public ConcurrentQueue<SentNotification> Sent { get; } = new();
    public ConcurrentQueue<string> AttemptKeys { get; } = new();
    public bool Fail { get; set; }
    public Task SendAsync(string recipient, string body, string deliveryKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AttemptKeys.Enqueue(deliveryKey);
        if (Fail) throw new InvalidOperationException("Synthetic email failure");
        Sent.Enqueue(new(recipient, body, deliveryKey));
        return Task.CompletedTask;
    }
}
