using System.Text;

namespace DentalApi.Services;

public static class StartupConfigurationValidator
{
    public static void Validate(IConfiguration config, string environmentName)
    {
        RequireKey(config, "Jwt:Key");
        Require(config, "Jwt:Issuer");
        Require(config, "Jwt:Audience");

        RequireKey(config, "PlatformJwt:Key");
        Require(config, "PlatformJwt:Issuer");
        Require(config, "PlatformJwt:Audience");

        if (config["Jwt:Key"] == config["PlatformJwt:Key"] ||
            config["Jwt:Issuer"] == config["PlatformJwt:Issuer"] ||
            config["Jwt:Audience"] == config["PlatformJwt:Audience"])
            throw new InvalidOperationException("Jwt болон PlatformJwt key, issuer, audience тусдаа байх ёстой.");

        Require(config, "ConnectionStrings:Default");

        // Integration тестүүд бодит SMTP-гүй, тусгаарлагдсан орчинд ажиллана.
        if (environmentName == "Testing") return;

        Require(config, "Email:Host");
        Require(config, "Email:Username");
        Require(config, "Email:Password");
        Require(config, "Email:From");

        if (!int.TryParse(config["Email:Port"], out var port) || port is < 1 or > 65535)
            throw new InvalidOperationException("Email:Port нь 1-65535 хооронд тоо байх ёстой.");

        if (!System.Net.Mail.MailAddress.TryCreate(config["Email:From"], out _))
            throw new InvalidOperationException("Email:From нь хүчинтэй имэйл хаяг байх ёстой.");
    }

    private static void RequireKey(IConfiguration config, string name)
    {
        Require(config, name);
        if (Encoding.UTF8.GetByteCount(config[name]!) < 32)
            throw new InvalidOperationException($"{name} нь дор хаяж 32 UTF-8 байт байх ёстой.");
    }

    private static void Require(IConfiguration config, string name)
    {
        if (string.IsNullOrWhiteSpace(config[name]))
            throw new InvalidOperationException($"Шаардлагатай тохиргоо дутуу: {name}.");
    }
}
