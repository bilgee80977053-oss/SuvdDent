using DentalApi.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DentalApi.IntegrationTests;

public sealed class StartupConfigurationValidatorTests
{
    [Theory]
    [InlineData("Jwt:Key")]
    [InlineData("PlatformJwt:Key")]
    [InlineData("Jwt:Issuer")]
    [InlineData("Email:Host")]
    [InlineData("Email:Password")]
    public void MissingRequiredSettingFailsWithItsName(string setting)
    {
        var values = ValidSettings();
        values.Remove(setting);

        var error = Assert.Throws<InvalidOperationException>(() =>
            StartupConfigurationValidator.Validate(Build(values), "Production"));
        Assert.Contains(setting, error.Message);
    }

    [Theory]
    [InlineData("Jwt:Key")]
    [InlineData("PlatformJwt:Key")]
    public void ShortSigningKeysAreRejected(string setting)
    {
        var values = ValidSettings();
        values[setting] = "short";
        var error = Assert.Throws<InvalidOperationException>(() =>
            StartupConfigurationValidator.Validate(Build(values), "Production"));
        Assert.Contains(setting, error.Message);
        Assert.DoesNotContain("short", error.Message);
    }

    [Fact]
    public void PlatformAndClinicIdentityMustBeDistinct()
    {
        var values = ValidSettings();
        values["PlatformJwt:Key"] = values["Jwt:Key"];
        Assert.Throws<InvalidOperationException>(() =>
            StartupConfigurationValidator.Validate(Build(values), "Production"));
    }

    [Fact]
    public void TestingDoesNotRequireRealEmailConfiguration()
    {
        var values = ValidSettings();
        foreach (var key in values.Keys.Where(k => k.StartsWith("Email:")).ToList())
            values.Remove(key);

        StartupConfigurationValidator.Validate(Build(values), "Testing");
    }

    [Fact]
    public void ProductionAcceptsValidConfiguration()
    {
        StartupConfigurationValidator.Validate(Build(ValidSettings()), "Production");
    }

    [Theory]
    [InlineData("*")]
    [InlineData("")]
    [InlineData("https://api.example.invalid")]
    public void UnsafeHostsAreRejected(string hosts)
    {
        var values = ValidSettings();
        values["AllowedHosts"] = hosts;
        Assert.Throws<InvalidOperationException>(() =>
            StartupConfigurationValidator.Validate(Build(values), "Production"));
    }

    [Fact]
    public void CorsOriginsMustHaveNoPathOrWildcard()
    {
        var values = ValidSettings();
        values["Cors:AllowedOrigins:0"] = "https://example.invalid/app";
        Assert.Throws<InvalidOperationException>(() =>
            StartupConfigurationValidator.Validate(Build(values), "Production"));
    }

    private static IConfiguration Build(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static Dictionary<string, string?> ValidSettings() => new()
    {
        ["ConnectionStrings:Default"] = "Data Source=:memory:",
        ["AllowedHosts"] = "localhost",
        ["Jwt:Key"] = "Synthetic-clinic-key-for-validator-tests-2026",
        ["Jwt:Issuer"] = "TestClinicIssuer",
        ["Jwt:Audience"] = "TestClinicAudience",
        ["PlatformJwt:Key"] = "Separate-synthetic-platform-key-for-validator-tests-2026",
        ["PlatformJwt:Issuer"] = "TestPlatformIssuer",
        ["PlatformJwt:Audience"] = "TestPlatformAudience",
        ["Email:Host"] = "smtp.example.invalid",
        ["Email:Port"] = "587",
        ["Email:From"] = "sender@example.invalid",
        ["Email:Username"] = "synthetic-user",
        ["Email:Password"] = "synthetic-password"
    };
}
