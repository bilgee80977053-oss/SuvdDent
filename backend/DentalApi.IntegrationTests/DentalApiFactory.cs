using DentalApi.Data;
using DentalApi.Models;
using DentalApi.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DentalApi.IntegrationTests;

// A fresh factory per test owns its own connection and synthetic data.
public sealed class DentalApiFactory : WebApplicationFactory<Program>
{
    public const string Password = "Synthetic-password-123!";
    public const string ResetCode = "123456";
    private readonly SqliteConnection connection = new("Data Source=:memory:");

    public DentalApiFactory() => connection.Open();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.Sources.Clear();
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Data Source=:memory:",
                ["Jwt:Key"] = "Synthetic-integration-test-signing-key-only-2026-0123456789",
                ["Jwt:Issuer"] = "DentalApi.IntegrationTests",
                ["Jwt:Audience"] = "DentalApi.IntegrationTests",
                ["Jwt:ExpireHours"] = "1"
            });
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<AppDbContext>();
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options => options.UseSqlite(connection));

            // No SMTP settings: accidental email sends fail while constructing
            // the sender address, before opening any network connection.
            services.RemoveAll<EmailService>();
            services.AddScoped(_ => new EmailService(new ConfigurationBuilder().Build()));
        });
    }

    public HttpClient CreateTestClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false
    });

    public async Task SeedAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (!ReferenceEquals(db.Database.GetDbConnection(), connection) ||
            new SqliteConnectionStringBuilder(connection.ConnectionString).DataSource != ":memory:")
            throw new InvalidOperationException("Tests may only use their owned SQLite in-memory connection.");
        await db.Database.EnsureCreatedAsync();
        if (await db.Users.AnyAsync() || await db.Clinics.AnyAsync())
            throw new InvalidOperationException("The test database must start empty; startup seeding ran.");

        db.Clinics.AddRange(new Clinic { Id = 101, Name = "Clinic A" },
                            new Clinic { Id = 202, Name = "Clinic B" });
        var hash = BCrypt.Net.BCrypt.HashPassword(Password, workFactor: 4);
        db.Users.AddRange(
            MakeUser(11, 101, "Patient A", Role.Patient, hash),
            MakeUser(12, 202, "Patient B", Role.Patient, hash),
            MakeUser(13, 101, "Doctor A", Role.Doctor, hash),
            MakeUser(14, 101, "Other patient A", Role.Patient, hash));
        db.Patients.AddRange(
            new Patient { Id = 21, UserId = 11, ClinicId = 101, FullName = "Patient A", Phone = "11111111", Address = "Synthetic A" },
            new Patient { Id = 22, UserId = 12, ClinicId = 202, FullName = "Patient B", Phone = "22222222", Address = "Synthetic B", MedicalNotes = "Synthetic note B" },
            new Patient { Id = 24, UserId = 14, ClinicId = 101, FullName = "Other patient A", Phone = "44444444" });
        db.VerificationCodes.Add(new VerificationCode
        {
            UserId = 11, Channel = "email", Purpose = "password-reset",
            CodeHash = BCrypt.Net.BCrypt.HashPassword(ResetCode, workFactor: 4),
            ExpiresAt = DateTime.UtcNow.AddMinutes(10)
        });
        await db.SaveChangesAsync();
    }

    private static User MakeUser(int id, int clinic, string name, Role role, string hash) => new()
    {
        Id = id, ClinicId = clinic, FullName = name, Email = $"user{id}@example.invalid",
        PasswordHash = hash, Role = role, IsActive = true, IsEmailVerified = true
    };

    // Snapshot every stored B user/patient field, not just the HTTP projection.
    public async Task<string> SnapshotBAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            Users = await db.Users.AsNoTracking().Where(u => u.ClinicId == 202).OrderBy(u => u.Id).ToListAsync(),
            Patients = await db.Patients.AsNoTracking().Where(p => p.ClinicId == 202).OrderBy(p => p.Id).ToListAsync()
        });
    }

    public string SignedTokenWithMismatch(string claim)
    {
        using var scope = Services.CreateScope();
        var user = scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.AsNoTracking().Single(u => u.Id == 11);
        if (claim == "clinicId") user.ClinicId = 202;
        else user.Role = Role.Director;
        return scope.ServiceProvider.GetRequiredService<JwtService>().Create(user);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) connection.Dispose();
    }
}