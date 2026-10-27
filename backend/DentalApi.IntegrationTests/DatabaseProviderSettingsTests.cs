using DentalApi.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DentalApi.IntegrationTests;

public sealed class DatabaseProviderSettingsTests
{
    [Theory]
    [InlineData(null, DatabaseProviderSettings.Sqlite)]
    [InlineData("Sqlite", DatabaseProviderSettings.Sqlite)]
    [InlineData("postgresql", DatabaseProviderSettings.PostgreSql)]
    public void ProviderSelectionIsExplicit(string? configured, string expected)
    {
        var config = Build(configured);
        Assert.Equal(expected, DatabaseProviderSettings.Selected(config));
    }

    [Fact]
    public void UnsupportedProviderIsRejected()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            DatabaseProviderSettings.Selected(Build("MSSQL")));
        Assert.Contains("Database:Provider", error.Message);
    }

    [Fact]
    public void PostgreSqlCannotAccidentallyRunSqliteMigrations()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            DatabaseProviderSettings.EnsureMigrationsReady(Build("PostgreSQL")));
        Assert.Contains("PostgreSQL", error.Message);
    }

    [Fact]
    public void SqliteCanStillRunItsExistingMigrations()
    {
        DatabaseProviderSettings.EnsureMigrationsReady(Build("Sqlite"));
        Assert.Equal("Data Source=:memory:", DatabaseProviderSettings.ConnectionString(Build("Sqlite")));
    }

    private static IConfiguration Build(string? provider) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = provider,
            ["ConnectionStrings:Default"] = "Data Source=:memory:"
        }).Build();
}
