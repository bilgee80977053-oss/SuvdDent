using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DentalApi.Contracts;
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

// Independent request connections allow actual concurrent HTTP updates.
// An anchor keeps this factory's unique shared in-memory database alive.
public class DoctorApiFactory : WebApplicationFactory<Program>
{
    private readonly string connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = "doctor-tests-" + Guid.NewGuid().ToString("N"),
        Mode = SqliteOpenMode.Memory, Cache = SqliteCacheMode.Shared,
        ForeignKeys = true, Pooling = false, DefaultTimeout = 1
    }.ToString();
    private readonly SqliteConnection anchor;
    public DoctorApiFactory()
    {
        anchor = new SqliteConnection(connectionString);
        anchor.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.Sources.Clear();
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = connectionString,
                ["AllowedHosts"] = "localhost",
                ["Jwt:Key"] = "Synthetic-doctor-tests-signing-key-only-01234567890123456789",
                ["Jwt:Issuer"] = "DoctorIntegrationTests",
                ["Jwt:Audience"] = "DoctorIntegrationTests",
                ["Jwt:ExpireHours"] = "1",
                ["PlatformJwt:Key"] = "Synthetic-separate-platform-key-for-doctor-tests-2026",
                ["PlatformJwt:Issuer"] = "DoctorTestPlatformIssuer",
                ["PlatformJwt:Audience"] = "DoctorTestPlatformAudience"
            });
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<AppDbContext>();
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.AddDbContext<AppDbContext>(o => o.UseSqlite(connectionString));
            services.RemoveAll<EmailService>();
            services.AddScoped(_ => new EmailService(new ConfigurationBuilder().Build()));
        });
    }

    public async Task SeedAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (new SqliteConnectionStringBuilder(db.Database.GetConnectionString()).Mode != SqliteOpenMode.Memory)
            throw new InvalidOperationException("Only in-memory databases are permitted.");
        await db.Database.MigrateAsync();
        if (await db.Users.AnyAsync()) throw new InvalidOperationException("Unexpected startup seed.");
        db.Clinics.AddRange(new Clinic { Id = 101, Name = "Clinic A" }, new Clinic { Id = 202, Name = "Clinic B" });
        var hash = BCrypt.Net.BCrypt.HashPassword(DentalApiFactory.Password, workFactor: 4);
        foreach (var (id, clinicId, role, active) in new[]
        {
            (11, 101, Role.Patient, true), (12, 202, Role.Patient, true),
            (13, 101, Role.Doctor, true), (15, 101, Role.Director, true),
            (16, 101, Role.Nurse, true), (17, 101, Role.Doctor, true),
            (18, 101, Role.Doctor, false), (19, 101, Role.Doctor, true),
            (20, 101, Role.Doctor, true), (21, 101, Role.Doctor, false),
            (23, 202, Role.Doctor, true), (25, 202, Role.Director, true)
        })
            db.Users.Add(new User { Id = id, ClinicId = clinicId, Role = role, IsActive = active,
                FullName = "Synthetic user " + id, Email = $"user{id}@example.invalid",
                Phone = id.ToString().PadLeft(8, '0'), PasswordHash = hash, IsEmailVerified = true });
        db.Patients.AddRange(
            new Patient { Id = 41, ClinicId = 101, UserId = 11, FullName = "Patient A", Phone = "11111111" },
            new Patient { Id = 42, ClinicId = 202, UserId = 12, FullName = "Patient B", Phone = "22222222" });
        foreach (var (id, clinicId, userId, published, active) in new[]
        {
            (31, 101, 13, true, true), (32, 202, 23, true, true),
            (33, 101, 17, false, true), (34, 101, 18, true, true), (35, 101, 19, true, false)
        })
            db.DoctorProfiles.Add(new DoctorProfile { Id = id, ClinicId = clinicId, UserId = userId,
                Specialty = "Dentistry", PracticeStartYear = 2015, Biography = "Synthetic bio",
                IsPublished = published, IsActive = active });
        db.DoctorWorkingHours.AddRange(
            new DoctorWorkingHours { ClinicId = 101, DoctorProfileId = 31, DayOfWeek = 1, StartMinute = 540, EndMinute = 780 },
            new DoctorWorkingHours { ClinicId = 202, DoctorProfileId = 32, DayOfWeek = 2, StartMinute = 600, EndMinute = 900 });
        await db.SaveChangesAsync();
    }

    public HttpClient Client() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
    });

    public async Task<HttpClient> AsAsync(int id, int clinicId = 101)
    {
        var client = Client();
        using var response = await client.PostAsJsonAsync("/api/Auth/login", new
        {
            identifier = $"user{id}@example.invalid", password = DentalApiFactory.Password, clinicId
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("token").GetString());
        return client;
    }

    public async Task<string> SnapshotAsync(int clinicId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return JsonSerializer.Serialize(new
        {
            Users = await db.Users.AsNoTracking().Where(u => u.ClinicId == clinicId).OrderBy(u => u.Id).ToArrayAsync(),
            Patients = await db.Patients.AsNoTracking().Where(p => p.ClinicId == clinicId).OrderBy(p => p.Id).ToArrayAsync(),
            Profiles = await db.DoctorProfiles.AsNoTracking().Where(p => p.ClinicId == clinicId).OrderBy(p => p.Id)
                .Select(p => new { p.Id, p.ClinicId, p.UserId, p.Specialty, p.PracticeStartYear, p.Biography, p.PhotoPath, p.IsActive, p.IsPublished, p.Version, p.CreatedAt, p.UpdatedAt }).ToArrayAsync(),
            Hours = await db.DoctorWorkingHours.AsNoTracking().Where(h => h.ClinicId == clinicId).OrderBy(h => h.Id).ToArrayAsync()
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) anchor.Dispose();
    }
}
