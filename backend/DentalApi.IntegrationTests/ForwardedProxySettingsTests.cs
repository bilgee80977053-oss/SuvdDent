using DentalApi.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DentalApi.IntegrationTests;

public sealed class ForwardedProxySettingsTests
{
    [Fact]
    public void DefaultsToDisabled()
    {
        var config = Build(new());
        ForwardedProxySettings.Validate(config);
        var options = new ForwardedHeadersOptions();
        ForwardedProxySettings.Configure(options, config);
        Assert.Equal(ForwardedHeaders.None, options.ForwardedHeaders);
    }

    [Fact]
    public void EnabledRequiresTrustedProxy()
    {
        var config = Build(new() { ["ForwardedHeaders:Enabled"] = "true" });
        Assert.Throws<InvalidOperationException>(() => ForwardedProxySettings.Validate(config));
    }

    [Theory]
    [InlineData("not-an-ip", "ForwardedHeaders:KnownProxies:0")]
    [InlineData("not-a-cidr", "ForwardedHeaders:KnownNetworks:0")]
    public void InvalidTrustedSourcesAreRejected(string value, string key)
    {
        var config = Build(new() { ["ForwardedHeaders:Enabled"] = "true", [key] = value });
        Assert.Throws<InvalidOperationException>(() => ForwardedProxySettings.Validate(config));
    }

    [Fact]
    public void OnlyConfiguredTrustedAddressesAreUsed()
    {
        var config = Build(new()
        {
            ["ForwardedHeaders:Enabled"] = "true",
            ["ForwardedHeaders:KnownProxies:0"] = "10.0.0.10",
            ["ForwardedHeaders:KnownNetworks:0"] = "192.168.8.0/24"
        });
        var options = new ForwardedHeadersOptions();
        ForwardedProxySettings.Configure(options, config);

        Assert.Equal(ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto, options.ForwardedHeaders);
        Assert.Equal(1, options.ForwardLimit);
        Assert.Single(options.KnownProxies);
        Assert.Single(options.KnownIPNetworks);
    }

    private static IConfiguration Build(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
