using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DentalApi.Data;

// A distinct context type isolates migration discovery and the model snapshot.
public sealed class PostgreSqlAppDbContext(DbContextOptions<PostgreSqlAppDbContext> options)
    : AppDbContext(options);

public sealed class PostgreSqlAppDbContextDesignTimeFactory
    : IDesignTimeDbContextFactory<PostgreSqlAppDbContext>
{
    public PostgreSqlAppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PostgreSqlAppDbContext>()
            // Model-only tooling: no credentials, configuration, startup, or database connection.
            .UseNpgsql("Host=localhost;Database=nexora_model_only")
            .Options);
}
