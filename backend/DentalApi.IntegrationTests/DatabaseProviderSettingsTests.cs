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
            DatabaseProviderSettings.EnsureMigrationsReady(Build("PostgreSQL"), ""));
        Assert.Contains("PostgreSQL", error.Message);
    }

    [Fact]
    public void SqliteCanStillRunItsExistingMigrations()
    {
        DatabaseProviderSettings.EnsureMigrationsReady(Build("Sqlite"));
        Assert.Equal("Data Source=:memory:", DatabaseProviderSettings.ConnectionString(Build("Sqlite")));
    }

    [Fact]
    public void PostgreSqlStartupRequiresExactExplicitApproval()
    {
        var config = Build("PostgreSQL");

        Assert.Throws<InvalidOperationException>(() =>
            DatabaseProviderSettings.EnsureMigrationsReady(config, "approved"));
        Assert.Throws<InvalidOperationException>(() =>
            DatabaseProviderSettings.EnsureMigrationsReady(config, "READ_ONLY_SCHEMA_CHECK "));
        DatabaseProviderSettings.EnsureMigrationsReady(
            config, DatabaseProviderSettings.PostgreSqlStartupApprovalValue);
    }

    [Fact]
    public void SqliteStartupDoesNotRequirePostgreSqlApproval()
    {
        DatabaseProviderSettings.EnsureMigrationsReady(Build("Sqlite"), "");
    }

    [Fact]
    public void PostgreSqlMigrationHistoryRequiresExactOrderedMatch()
    {
        string[] expected = ["baseline", "appointments", "notifications"];
        DatabaseProviderSettings.VerifyPostgreSqlMigrationHistory(
            expected, ["baseline", "appointments", "notifications"]);

        foreach (string[] applied in new string[][] {
            [],
            ["baseline"],
            ["baseline", "notifications"],
            ["baseline", "notifications", "appointments"],
            ["baseline", "appointments", "notifications", "unexpected"]
        })
        {
            var error = Assert.Throws<InvalidOperationException>(() =>
                DatabaseProviderSettings.VerifyPostgreSqlMigrationHistory(expected, applied));
            Assert.Contains("migration history", error.Message);
        }

        Assert.Throws<InvalidOperationException>(() =>
            DatabaseProviderSettings.VerifyPostgreSqlMigrationHistory([], []));
    }

    [Fact]
    public async Task PostgreSqlSchemaVerificationRejectsSqliteBeforeAnyDatabaseAccess()
    {
        using var sqlite = new DentalApi.Data.AppDbContextDesignTimeFactory().CreateDbContext([]);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => DatabaseProviderSettings.VerifyPostgreSqlSchemaAsync(sqlite));
    }

    private static IConfiguration Build(string? provider) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = provider,
            ["ConnectionStrings:Default"] = "Data Source=:memory:"
        }).Build();
}
