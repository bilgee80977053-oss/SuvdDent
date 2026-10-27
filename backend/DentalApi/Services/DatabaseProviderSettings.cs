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

    public static void EnsureMigrationsReady(IConfiguration config)
    {
        // Until the separate PostgreSQL baseline exists, never run old SQLite migrations on it.
        if (Selected(config) == PostgreSql)
            throw new InvalidOperationException(
                "PostgreSQL-ийн тусдаа baseline migration бэлэн болтол startup migration хаалттай.");
    }
}
