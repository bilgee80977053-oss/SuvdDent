using System.Collections.Concurrent;
using DentalApi.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DentalApi.IntegrationTests;

public sealed class FakePlatformMessageSender : IPlatformMessageSender
{
    public bool IsAvailable => true;
    public ConcurrentQueue<(string Channel, string Destination, string Purpose, string Code)> Messages { get; } = new();
    public Task SendAsync(string channel, string destination, string purpose, string code)
    { Messages.Enqueue((channel, destination, purpose, code)); return Task.CompletedTask; }
    public string Code(string destination, string purpose = "registration") =>
        Messages.Last(m => m.Destination == destination && m.Purpose == purpose).Code;
}

public sealed class PlatformApiFactory : DoctorApiFactory
{
    public FakePlatformMessageSender Messages { get; } = new();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PlatformJwt:Key"] = "Synthetic-platform-tests-independent-key-01234567890123456789",
            ["PlatformJwt:Issuer"] = "NexoraPlatformTests", ["PlatformJwt:Audience"] = "NexoraPlatformCustomersTests"
        }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IPlatformMessageSender>();
            services.AddSingleton<IPlatformMessageSender>(Messages);
        });
    }
}
