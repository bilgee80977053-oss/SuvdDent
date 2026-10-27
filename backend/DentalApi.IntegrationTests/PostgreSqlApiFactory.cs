using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DentalApi.Contracts;
using DentalApi.Data;
using DentalApi.Models;
using DentalApi.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DentalApi.IntegrationTests;

// Each test owns two generated clinic IDs and one platform account. HTTP requests
// use independent contexts, so cleanup explicitly removes only this fixture's rows.
internal sealed class PostgreSqlApiFactory : WebApplicationFactory<Program>
{
    private readonly string connectionString;
    internal string Marker { get; } = "pg-step4-" + Guid.NewGuid().ToString("N");
    internal Clinic ClinicA { get; private set; } = null!;
    internal Clinic ClinicB { get; private set; } = null!;
    internal User PatientA { get; private set; } = null!;
    internal User PatientB { get; private set; } = null!;
    internal User DoctorA { get; private set; } = null!;
    internal User DoctorB { get; private set; } = null!;
    internal User DirectorA { get; private set; } = null!;
    internal User NewDoctorA { get; private set; } = null!;
    internal DoctorProfile ProfileA { get; private set; } = null!;
    internal DoctorProfile ProfileB { get; private set; } = null!;
    internal Service ServiceA { get; private set; } = null!;
    internal Service ServiceA2 { get; private set; } = null!;
    internal Service ServiceB { get; private set; } = null!;
    internal PlatformAccount Platform { get; private set; } = null!;
    private bool cleaned;
    private PostgreSqlApiFactory(string connectionString) => this.connectionString = connectionString;

    internal static async Task<PostgreSqlApiFactory> StartAsync()
    {
        var target = PostgreSqlTestTarget.FromEnvironment();
        await PostgreSqlTestTarget.VerifyAsync(target); // Must precede every fixture write and host startup.
        var app = new PostgreSqlApiFactory(target);
        try { await app.SeedAsync(); return app; }
        catch
        {
            await app.DisposeAsync();
            throw new InvalidOperationException("PostgreSQL synthetic fixture setup failed; inspect the schema and permissions. Credentials and provider details are suppressed.");
        }
    }

    internal PostgreSqlAppDbContext Context(params IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<PostgreSqlAppDbContext>()
            .UseNpgsql(connectionString).AddInterceptors(interceptors).Options);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.Sources.Clear();
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Retain the existing Testing configuration contract. The DB services
                // below replace it with Npgsql, without reading production connection variables.
                ["Database:Provider"] = "Sqlite",
                ["ConnectionStrings:Default"] = "Data Source=:memory:",
                ["AllowedHosts"] = "localhost",
                ["Jwt:Key"] = "Synthetic-PostgreSQL-clinic-signing-key-only-2026-0123456789",
                ["Jwt:Issuer"] = "PostgreSqlClinicTests",
                ["Jwt:Audience"] = "PostgreSqlClinicTests",
                ["Jwt:ExpireHours"] = "1",
                ["PlatformJwt:Key"] = "Synthetic-separate-PostgreSQL-platform-key-only-0123456789",
                ["PlatformJwt:Issuer"] = "PostgreSqlPlatformTests",
                ["PlatformJwt:Audience"] = "PostgreSqlPlatformTests"
            });
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<AppDbContext>();
            services.RemoveAll<PostgreSqlAppDbContext>();
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<DbContextOptions<PostgreSqlAppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<PostgreSqlAppDbContext>>();
            services.AddDbContext<AppDbContext, PostgreSqlAppDbContext>(o => o.UseNpgsql(connectionString));
            services.RemoveAll<EmailService>();
            services.AddScoped(_ => new EmailService(new ConfigurationBuilder().Build()));
        });
    }

    private async Task SeedAsync()
    {
        await using var db = Context();
        await using var tx = await db.Database.BeginTransactionAsync();
        ClinicA = new Clinic { Name = Marker + "-A" };
        ClinicB = new Clinic { Name = Marker + "-B" };
        db.Clinics.AddRange(ClinicA, ClinicB);
        await db.SaveChangesAsync();
        User Make(Clinic clinic, Role role, string suffix) => new()
        {
            ClinicId = clinic.Id, FullName = Marker + suffix, Email = Marker + suffix + "@example.invalid",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(DentalApiFactory.Password, workFactor: 4),
            Role = role, IsActive = true, IsEmailVerified = true, CreatedAt = DateTime.UtcNow
        };
        PatientA = Make(ClinicA, Role.Patient, "-patient-a");
        PatientB = Make(ClinicB, Role.Patient, "-patient-b");
        DoctorA = Make(ClinicA, Role.Doctor, "-doctor-a");
        DoctorB = Make(ClinicB, Role.Doctor, "-doctor-b");
        NewDoctorA = Make(ClinicA, Role.Doctor, "-new-doctor");
        DirectorA = Make(ClinicA, Role.Director, "-director");
        db.Users.AddRange(PatientA, PatientB, DoctorA, DoctorB, NewDoctorA, DirectorA);
        await db.SaveChangesAsync();
        ProfileA = new DoctorProfile { ClinicId = ClinicA.Id, UserId = DoctorA.Id, Specialty = "Synthetic A", PracticeStartYear = 2015, IsPublished = true };
        ProfileB = new DoctorProfile { ClinicId = ClinicB.Id, UserId = DoctorB.Id, Specialty = "Synthetic B", PracticeStartYear = 2015, IsPublished = true };
        db.DoctorProfiles.AddRange(ProfileA, ProfileB);
        db.Patients.Add(new Patient { ClinicId = ClinicB.Id, UserId = PatientB.Id, FullName = Marker + "-patient-b", Phone = "99000002" });
        ServiceA = new Service { ClinicId = ClinicA.Id, Name = Marker + "-service-a" };
        ServiceA2 = new Service { ClinicId = ClinicA.Id, Name = Marker + "-service-a2" };
        ServiceB = new Service { ClinicId = ClinicB.Id, Name = Marker + "-service-b" };
        db.Services.AddRange(ServiceA, ServiceA2, ServiceB);
        Platform = new PlatformAccount { FullName = Marker, Email = Marker + "@example.invalid", PasswordHash = "synthetic-unused" };
        db.PlatformAccounts.Add(Platform);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    internal HttpClient Client(User? user = null)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        if (user is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Services.GetRequiredService<JwtService>().Create(user));
        return client;
    }

    internal async Task<int> CreatePatientAsync()
    {
        using var client = Client(PatientA);
        using var response = await client.PostAsJsonAsync("/api/Patients/me", new
        { fullName = Marker + "-patient-a", phone = "99000001", dateOfBirth = "2000-02-03T00:00:00", address = "Synthetic address" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await using var db = Context();
        return (await db.Patients.SingleAsync(p => p.ClinicId == ClinicA.Id && p.UserId == PatientA.Id)).Id;
    }

    internal async Task<string> SnapshotBAsync()
    {
        await using var db = Context();
        var clinic = ClinicB.Id;
        return JsonSerializer.Serialize(new
        {
            Clinics = await db.Clinics.AsNoTracking().Where(c => c.Id == clinic).ToArrayAsync(),
            Users = await db.Users.AsNoTracking().Where(u => u.ClinicId == clinic).OrderBy(u => u.Id).ToArrayAsync(),
            Patients = await db.Patients.AsNoTracking().Where(p => p.ClinicId == clinic).OrderBy(p => p.Id).ToArrayAsync(),
            Profiles = await db.DoctorProfiles.AsNoTracking().Where(d => d.ClinicId == clinic).OrderBy(d => d.Id).ToArrayAsync(),
            Hours = await db.DoctorWorkingHours.AsNoTracking().Where(h => h.ClinicId == clinic).OrderBy(h => h.Id).ToArrayAsync(),
            Services = await db.Services.AsNoTracking().Where(s => s.ClinicId == clinic).OrderBy(s => s.Id).ToArrayAsync(),
            Assignments = await db.DoctorServices.AsNoTracking().Where(s => s.ClinicId == clinic).OrderBy(s => s.Id).ToArrayAsync(),
            Schedules = await db.PatientAvailabilitySchedules.AsNoTracking().Where(s => s.ClinicId == clinic).OrderBy(s => s.PatientId).ToArrayAsync(),
            Availability = await db.PatientAvailabilities.AsNoTracking().Where(s => s.ClinicId == clinic).OrderBy(s => s.Id).ToArrayAsync()
        });
    }

    public override async ValueTask DisposeAsync()
    {
        try { await CleanupAsync(); }
        finally { await base.DisposeAsync(); }
    }

    private async Task CleanupAsync()
    {
        if (cleaned || ClinicA is null || ClinicA.Id == 0) return;
        try
        {
            await PostgreSqlTestTarget.VerifyAsync(connectionString);
            await using var db = Context();
            await using var tx = await db.Database.BeginTransactionAsync();
            var ids = new[] { ClinicA.Id, ClinicB.Id }.Where(id => id > 0).ToArray();
            var clinics = await db.Clinics.Where(c => ids.Contains(c.Id)).ToArrayAsync();
            if (clinics.Any(c => c.Name != Marker + (c.Id == ClinicA.Id ? "-A" : "-B")))
                throw new InvalidOperationException(); // Ownership changed: fail closed, delete nothing.
            if (Platform is not null && Platform.Id > 0 &&
                await db.PlatformAccounts.AnyAsync(a => a.Id == Platform.Id &&
                    (a.Email == null || a.Email != Marker + "@example.invalid" || a.FullName != Marker)))
                throw new InvalidOperationException();
            var users = db.Users.Where(u => ids.Contains(u.ClinicId)).Select(u => u.Id);
            await db.ClinicPatientLinks.Where(x => ids.Contains(x.ClinicId)).ExecuteDeleteAsync();
            await db.PatientAvailabilities.Where(x => ids.Contains(x.ClinicId)).ExecuteDeleteAsync();
            await db.PatientAvailabilitySchedules.Where(x => ids.Contains(x.ClinicId)).ExecuteDeleteAsync();
            await db.DoctorServices.Where(x => ids.Contains(x.ClinicId)).ExecuteDeleteAsync();
            await db.DoctorWorkingHours.Where(x => ids.Contains(x.ClinicId)).ExecuteDeleteAsync();
            await db.DoctorProfiles.Where(x => ids.Contains(x.ClinicId)).ExecuteDeleteAsync();
            await db.Patients.Where(x => ids.Contains(x.ClinicId)).ExecuteDeleteAsync();
            await db.VerificationCodes.Where(x => users.Contains(x.UserId)).ExecuteDeleteAsync();
            await db.Users.Where(x => ids.Contains(x.ClinicId)).ExecuteDeleteAsync();
            await db.Services.Where(x => ids.Contains(x.ClinicId)).ExecuteDeleteAsync();
            await db.Clinics.Where(c => ids.Contains(c.Id)).ExecuteDeleteAsync();
            if (Platform is not null && Platform.Id > 0)
            {
                await db.PlatformVerificationCodes.Where(x => x.PlatformAccountId == Platform.Id).ExecuteDeleteAsync();
                await db.PlatformAccounts.Where(x => x.Id == Platform.Id).ExecuteDeleteAsync();
            }
            if (await db.Clinics.AnyAsync(c => ids.Contains(c.Id)) ||
                await db.Users.AnyAsync(u => ids.Contains(u.ClinicId)) ||
                await db.Services.AnyAsync(s => ids.Contains(s.ClinicId)) ||
                (Platform is not null && await db.PlatformAccounts.AnyAsync(a => a.Id == Platform.Id)))
                throw new InvalidOperationException();
            await tx.CommitAsync();
            cleaned = true;
        }
        catch
        {
            throw new InvalidOperationException("PostgreSQL test cleanup failed for run " + Marker + ". Only owned rows were targeted; inspect this run before retrying. No schema reset is permitted.");
        }
    }
}
