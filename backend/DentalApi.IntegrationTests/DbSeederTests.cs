using DentalApi.Data;
using DentalApi.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DentalApi.IntegrationTests;

public sealed class DbSeederTests
{
    [Fact]
    public void MissingCredentialsDoNotCreateAnyRecords()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = CreateDatabase(connection);

        Assert.False(DbSeeder.Seed(db, null, null, NullLogger.Instance));
        Assert.False(DbSeeder.Seed(db, "88112233", null, NullLogger.Instance));
        Assert.Empty(db.Clinics);
        Assert.Empty(db.Users);
    }

    [Fact]
    public void ExplicitCredentialsSeedOnceWithoutUsingADefaultPassword()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = CreateDatabase(connection);

        const string password = "Synthetic-seed-only-password-2026!";
        Assert.True(DbSeeder.Seed(db, "88112233", password, NullLogger.Instance));

        var director = Assert.Single(db.Users);
        Assert.Equal("88112233", director.Phone);
        Assert.Equal(Role.Director, director.Role);
        Assert.True(BCrypt.Net.BCrypt.Verify(password, director.PasswordHash));
        Assert.False(BCrypt.Net.BCrypt.Verify("Not-the-seed-password", director.PasswordHash));
        Assert.Equal(3, db.Services.Count());
        Assert.False(DbSeeder.Seed(db, "99112233", "Other-synthetic-password", NullLogger.Instance));
        Assert.Single(db.Users);
    }

    private static AppDbContext CreateDatabase(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection).Options;
        var db = new AppDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }
}
