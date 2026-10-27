using DentalApi.Data;
using DentalApi.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace DentalApi.IntegrationTests;

public sealed class DatabaseHealthCheckTests
{
    [Fact]
    public async Task AnonymousHealthEndpointReportsOnlyGenericStatus()
    {
        using var factory = new DentalApiFactory();
        await factory.SeedAsync();
        using var client = factory.CreateTestClient();

        using var response = await client.GetAsync("/health");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("Healthy", body);
        Assert.DoesNotContain("Data Source", body);
    }

    [Fact]
    public async Task DatabaseCheckReturnsUnhealthyWithoutExceptionDetails()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"nexora-missing-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder
            {
                DataSource = missingPath,
                Mode = SqliteOpenMode.ReadOnly
            }.ToString()).Options;
        using var db = new AppDbContext(options);

        var result = await new DatabaseHealthCheck(db).CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Null(result.Exception);
        Assert.Null(result.Description);
    }
}
