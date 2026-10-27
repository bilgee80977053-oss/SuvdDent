using Npgsql;
using Xunit;

namespace DentalApi.IntegrationTests;

public sealed class PostgreSqlFactAttribute : FactAttribute
{
    public PostgreSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgreSqlTestTarget.Variable)))
            Skip = "Real PostgreSQL not executed: NEXORA_PG_TEST_CONNECTION is absent.";
    }
}

internal static class PostgreSqlTestTarget
{
    internal const string Variable = "NEXORA_PG_TEST_CONNECTION";
    internal const string Baseline = "20261027132306_PostgreSqlBaseline";

    internal static string FromEnvironment() => Validate(Environment.GetEnvironmentVariable(Variable));

    internal static string Validate(string? value)
    {
        NpgsqlConnectionStringBuilder settings;
        try
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException();
            settings = new NpgsqlConnectionStringBuilder(value);
        }
        catch
        {
            // Do not attach parser exceptions: their messages may contain input values.
            throw new InvalidOperationException("An explicit valid NEXORA_PG_TEST_CONNECTION is required.");
        }
        if (settings.Host != "127.0.0.1" || settings.Port != 5432 ||
            settings.Database != "nexora_dental_test" || settings.Username != "nexora_dev" ||
            !string.IsNullOrEmpty(settings.Options) ||
            (!string.IsNullOrEmpty(settings.SearchPath) && settings.SearchPath != "public"))
            throw new InvalidOperationException("PostgreSQL test target rejected; no connection was opened.");

        settings.Pooling = false; // Independent physical connections; no shared transaction/session state.
        settings.Enlist = false;
        settings.SearchPath = "public";
        settings.IncludeErrorDetail = false;
        settings.LogParameters = false;
        settings.Timeout = 5;
        settings.CommandTimeout = 15;
        settings.ApplicationName = "Nexora disposable integration tests";
        return settings.ConnectionString;
    }

    internal static async Task VerifyAsync(string connectionString)
    {
        connectionString = Validate(connectionString);
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using (var identity = new NpgsqlCommand(
                "SELECT current_database(), current_user, current_setting('server_version_num')::int", connection))
            await using (var reader = await identity.ExecuteReaderAsync())
            {
                if (!await reader.ReadAsync() || reader.GetString(0) != "nexora_dental_test" ||
                    reader.GetString(1) != "nexora_dev" || reader.GetInt32(2) is < 180000 or >= 190000)
                    throw new InvalidOperationException();
            }
            await using var history = new NpgsqlCommand(
                "SELECT COUNT(*) FROM public.\"__EFMigrationsHistory\" WHERE \"MigrationId\" = @baseline", connection);
            history.Parameters.AddWithValue("baseline", Baseline);
            if ((long)(await history.ExecuteScalarAsync())! != 1) throw new InvalidOperationException();
        }
        catch
        {
            throw new InvalidOperationException("PostgreSQL test preflight failed. Verify the approved local target, PostgreSQL 18, access and baseline; no test writes were attempted.");
        }
    }
}

[CollectionDefinition("Real PostgreSQL", DisableParallelization = true)]
public sealed class PostgreSqlCollection;
