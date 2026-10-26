using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DentalApi.Data;

// Schema tooling must never start the web app, its seeder, or a real database.
// Commands only needing the model use this isolated, nonpersistent database.
public sealed class AppDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:;Foreign Keys=True")
            .Options);
}