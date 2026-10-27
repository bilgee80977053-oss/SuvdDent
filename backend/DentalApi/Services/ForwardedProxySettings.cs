using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace DentalApi.Services;

public static class ForwardedProxySettings
{
    public static bool Enabled(IConfiguration config) =>
        config.GetValue<bool>("ForwardedHeaders:Enabled");

    public static void Validate(IConfiguration config)
    {
        if (!Enabled(config)) return;

        var proxies = Values(config, "ForwardedHeaders:KnownProxies");
        var networks = Values(config, "ForwardedHeaders:KnownNetworks");
        if (proxies.Length == 0 && networks.Length == 0)
            throw new InvalidOperationException(
                "ForwardedHeaders:Enabled үед KnownProxies эсвэл KnownNetworks заавал тохируулна.");

        if (proxies.Any(p => !IPAddress.TryParse(p, out _)))
            throw new InvalidOperationException("ForwardedHeaders:KnownProxies-д зөв IP хаяг оруулна.");

        if (networks.Any(n => !System.Net.IPNetwork.TryParse(n, out _)))
            throw new InvalidOperationException("ForwardedHeaders:KnownNetworks-д CIDR формат ашиглана.");
    }

    public static void Configure(ForwardedHeadersOptions options, IConfiguration config)
    {
        if (!Enabled(config)) return;
        Validate(config);

        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = 1;
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach (var proxy in Values(config, "ForwardedHeaders:KnownProxies"))
            options.KnownProxies.Add(IPAddress.Parse(proxy));
        foreach (var network in Values(config, "ForwardedHeaders:KnownNetworks"))
            options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
    }

    private static string[] Values(IConfiguration config, string key) =>
        config.GetSection(key).GetChildren().Select(c => c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim()).ToArray();
}
