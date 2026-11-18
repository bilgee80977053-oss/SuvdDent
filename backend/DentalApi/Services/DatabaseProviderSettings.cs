using DentalApi.Data;
using Microsoft.EntityFrameworkCore;

namespace DentalApi.Services;

public static class DatabaseProviderSettings
{
    public const string Sqlite = "Sqlite";
    public const string PostgreSql = "PostgreSQL";

    public static string Selected(IConfiguration config)
    {
        var provider = config["Database:Provider"]?.Trim();
        if (string.IsNullOrEmpty(provider) ||
            provider.Equals(Sqlite, StringComparison.OrdinalIgnoreCase))
            return Sqlite;
        if (provider.Equals(PostgreSql, StringComparison.OrdinalIgnoreCase))
            return PostgreSql;

        throw new InvalidOperationException(
            "Database:Provider нь зөвхөн Sqlite эсвэл PostgreSQL байж болно.");
    }

    public static string ConnectionString(IConfiguration config)
    {
        if (Selected(config) == PostgreSql)
        {
            // PostgreSQL credentials must never be read from appsettings files.
            var value = Environment.GetEnvironmentVariable("ConnectionStrings__Default");
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException(
                    "PostgreSQL холболтын ConnectionStrings__Default environment variable шаардлагатай.");
            return value;
        }

        var sqlite = config.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(sqlite))
            throw new InvalidOperationException("Шаардлагатай тохиргоо дутуу: ConnectionStrings:Default.");
        return sqlite;
    }

    public static void Configure(DbContextOptionsBuilder options, IConfiguration config)
    {
        var connection = ConnectionString(config);
        if (Selected(config) == PostgreSql)
            options.UseNpgsql(connection);
        else
            options.UseSqlite(connection);
    }

    // A deliberate opt-in is required even for a pre-migrated PostgreSQL database.
    // This flag authorizes starting the API, never schema updates or seeding.
    public const string PostgreSqlStartupApprovalVariable = "NEXORA_PG_STARTUP_APPROVAL";
    public const string PostgreSqlStartupApprovalValue = "READ_ONLY_SCHEMA_CHECK";

    public static void EnsureMigrationsReady(IConfiguration config, string? approvalOverride = null)
    {
        if (Selected(config) == PostgreSql &&
            !string.Equals(
                approvalOverride ?? Environment.GetEnvironmentVariable(PostgreSqlStartupApprovalVariable),
                PostgreSqlStartupApprovalValue,
                StringComparison.Ordinal))
            throw new InvalidOperationException(
                "PostgreSQL API startup is blocked. An explicit read-only schema-check approval is required; " +
                "application startup never applies PostgreSQL migrations.");
    }

    // Compare the entire applied history, not only the last migration. Fail closed
    // for an empty, incomplete, out-of-order or unexpected history.
    public static void VerifyPostgreSqlMigrationHistory(
        IReadOnlyList<string> expected, IReadOnlyList<string> applied)
    {
        if (expected.Count == 0 || !expected.SequenceEqual(applied, StringComparer.Ordinal))
            throw new InvalidOperationException(
                "PostgreSQL schema migration history does not exactly match this application. " +
                "Startup stopped without running migrations or seeding.");
    }

    public static async Task VerifyPostgreSqlSchemaAsync(
        AppDbContext db, CancellationToken cancellationToken = default)
    {
        if (db is not PostgreSqlAppDbContext || !db.Database.IsNpgsql())
            throw new InvalidOperationException(
                "PostgreSQL startup requires the dedicated PostgreSqlAppDbContext.");

        // EF Core metadata is local. Only GetAppliedMigrationsAsync reads the
        // server's EF migration history. Do not call Migrate/EnsureCreated here.
        var expected = db.Database.GetMigrations().ToArray();
        var applied = (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).ToArray();
        VerifyPostgreSqlMigrationHistory(expected, applied);
    }
}
