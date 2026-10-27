using Npgsql;
using Xunit;

namespace DentalApi.IntegrationTests;

public sealed class PostgreSqlTargetSafetyTests
{
    // Synthetic target metadata only. These tests never open a connection.
    private const string Approved = "Host=127.0.0.1;Port=5432;Database=nexora_dental_test;Username=nexora_dev";

    [Theory]
    [InlineData("Host", "localhost")]
    [InlineData("Host", "127.0.0.1,localhost")]
    [InlineData("Host", "::1")]
    [InlineData("Port", "5433")]
    [InlineData("Database", "nexora_dental_dev")]
    [InlineData("Database", "postgres")]
    [InlineData("Username", "postgres")]
    [InlineData("Search Path", "other_schema")]
    [InlineData("Options", "-c role=postgres")]
    public void Rejects_unapproved_targets_before_connecting(string key, string value)
    {
        var builder = new NpgsqlConnectionStringBuilder(Approved) { [key] = value };
        Assert.Throws<InvalidOperationException>(() => PostgreSqlTestTarget.Validate(builder.ConnectionString));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("synthetic-private-parser-input")]
    public void Missing_or_malformed_configuration_fails_without_echoing_input(string? input)
    {
        var error = Assert.Throws<InvalidOperationException>(() => PostgreSqlTestTarget.Validate(input));
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("synthetic-private-parser-input", error.ToString());
    }

    [Fact]
    public void Approved_target_disables_pooling_parameter_logging_details_and_ambient_transactions()
    {
        var builder = new NpgsqlConnectionStringBuilder(PostgreSqlTestTarget.Validate(Approved + ";Include Error Detail=true;Log Parameters=true;Pooling=true;Enlist=true"));
        Assert.Equal("nexora_dental_test", builder.Database);
        Assert.Equal("public", builder.SearchPath);
        Assert.False(builder.Pooling);
        Assert.False(builder.IncludeErrorDetail);
        Assert.False(builder.LogParameters);
        Assert.False(builder.Enlist);
    }
}
