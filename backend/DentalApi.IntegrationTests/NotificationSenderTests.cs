using System.Net;
using System.Text.Json;
using DentalApi.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DentalApi.IntegrationTests;

public sealed class NotificationSenderTests
{
    private sealed class Gateway : HttpMessageHandler
    {
        internal int Calls;
        internal string? Key;
        internal string? Authorization;
        internal string? Body;
        internal HttpStatusCode Status = HttpStatusCode.Accepted;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Key = Assert.Single(request.Headers.GetValues("Idempotency-Key"));
            Authorization = request.Headers.Authorization?.ToString();
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(Status);
        }
    }
    private static IConfiguration Settings(Dictionary<string,string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public async Task Sms_gateway_sends_expected_payload_and_stable_key_without_network()
    {
        using var gateway = new Gateway();
        using var client = new HttpClient(gateway);
        var sender = new GatewaySmsSender(client, Settings(new()
        {
            ["Notifications:Sms:Enabled"] = "true",
            ["Notifications:Sms:Endpoint"] = "https://gateway.example.invalid/notifications",
            ["Notifications:Sms:Token"] = "synthetic-test-token"
        }));
        await sender.SendAsync("+97600000011", "Таны захиалга баталгаажлаа.", "stable-key", default);
        Assert.Equal("stable-key", gateway.Key);
        Assert.Equal("Bearer synthetic-test-token", gateway.Authorization);
        using var body = JsonDocument.Parse(gateway.Body!);
        Assert.Equal("+97600000011", body.RootElement.GetProperty("recipient").GetString());
        Assert.Equal("Таны захиалга баталгаажлаа.", body.RootElement.GetProperty("message").GetString());
        gateway.Status = HttpStatusCode.ServiceUnavailable;
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            sender.SendAsync("+97600000011", "Synthetic", "stable-key", default));
        Assert.Equal("stable-key", gateway.Key);
    }

    [Theory]
    [InlineData(false, "https://gateway.example.invalid")]
    [InlineData(true, "http://gateway.example.invalid")]
    [InlineData(true, "invalid")]
    public async Task Sms_requires_explicit_enablement_and_https(bool enabled, string endpoint)
    {
        using var gateway = new Gateway();
        using var client = new HttpClient(gateway);
        var sender = new GatewaySmsSender(client, Settings(new()
        {
            ["Notifications:Sms:Enabled"] = enabled.ToString(),
            ["Notifications:Sms:Endpoint"] = endpoint
        }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync("Synthetic", "Synthetic", "key", default));
        Assert.Equal(0, gateway.Calls);
    }

    [Fact]
    public async Task Disabled_email_never_attempts_smtp()
    {
        var config = Settings(new());
        var sender = new NotificationEmailSender(new EmailService(config), config);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sender.SendAsync("synthetic@example.invalid", "Synthetic", "key", default));
    }
}
