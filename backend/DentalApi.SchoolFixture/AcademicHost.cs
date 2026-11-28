using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using DentalApi.Data;
using DentalApi.Models;
using DentalApi.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DentalApi.SchoolFixture;

/// <summary>A separate classroom runner; never started by the deployed API.</summary>
internal sealed class AcademicHost : WebApplicationFactory<Program>
{
    private const int Port = 5018;
    private static readonly Uri Origin = new($"http://127.0.0.1:{Port}");
    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = $"suvddent-academic-{Guid.NewGuid():N}",
        Mode = SqliteOpenMode.Memory, Cache = SqliteCacheMode.Shared,
        Pooling = false, ForeignKeys = true
    }.ToString();
    private readonly string _clinicKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    private readonly string _platformKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

    internal static async Task<int> RunAsync()
    {
        if (Environment.GetEnvironmentVariable("NEXORA_PG_TEST_CONNECTION") is not null ||
            Environment.GetEnvironmentVariable("PGPASSWORD") is not null)
            throw new InvalidOperationException("Remove PostgreSQL test and password variables before starting the classroom runner.");

        await using var host = new AcademicHost();
        // An exclusively owned anchor keeps data in memory until this process exits.
        await using var anchor = new SqliteConnection(host._connectionString);
        await anchor.OpenAsync();
        host.UseKestrel(options => options.Listen(IPAddress.Loopback, Port));
        var password = "Demo-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(6)) + "!";
        var clinicId = await host.SeedAsync(password);
        using var client = host.CreateDefaultClient();
        if (client.BaseAddress != Origin)
            throw new InvalidOperationException("Refusing unexpected academic host address.");

        Console.WriteLine("SUVDDENT ACADEMIC READY");
        Console.WriteLine($"API: {Origin.GetLeftPart(UriPartial.Authority)} | Clinic ID: {clinicId}");
        Console.WriteLine("Synthetic accounts (same password):");
        Console.WriteLine("  school-patient@example.invalid");
        Console.WriteLine("  school-director@example.invalid");
        Console.WriteLine("  school-doctor@example.invalid");
        Console.WriteLine($"This run's synthetic password: {password}");
        Console.WriteLine("Use only invented names and @example.invalid addresses. Registration codes appear here; nothing is sent.");
        Console.WriteLine("Ctrl+C exits. No classroom records survive restart.");
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ConsoleCancelEventHandler stop = (_, e) => { e.Cancel = true; completion.TrySetResult(); };
        Console.CancelKeyPress += stop;
        try { await completion.Task; }
        finally { Console.CancelKeyPress -= stop; }
        return 0;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            // Do not inherit user secrets, database/SMTP credentials, TLS forwarding or listeners.
            configuration.Sources.Clear();
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "Sqlite",
                ["ConnectionStrings:Default"] = _connectionString,
                ["AllowedHosts"] = "127.0.0.1;localhost",
                ["Cors:AllowedOrigins:0"] = "http://localhost:7357",
                ["Cors:AllowedOrigins:1"] = "http://127.0.0.1:7357",
                ["Jwt:Key"] = _clinicKey,
                ["Jwt:Issuer"] = "SuvdDentAcademicClinic",
                ["Jwt:Audience"] = "SuvdDentAcademicPatient",
                ["Jwt:ExpireHours"] = "2",
                ["PlatformJwt:Key"] = _platformKey,
                ["PlatformJwt:Issuer"] = "SuvdDentAcademicPlatform",
                ["PlatformJwt:Audience"] = "SuvdDentAcademicPlatformClient",
                ["Logging:LogLevel:Default"] = "Warning",
                ["Features:ClinicalTreatment"] = "true",
                ["Features:PerformedProcedureAcceptance"] = "true",
                ["Features:AssignedEncounterAcceptance"] = "true",
                ["Features:PatientHistoryAcceptance"] = "true"
            });
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<EmailService>();
            services.AddScoped<EmailService, AcademicEmailSink>();
        });
    }

    private async Task<int> SeedAsync(string password)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actual = new SqliteConnectionStringBuilder(db.Database.GetConnectionString());
        if (!db.Database.IsSqlite() || actual.ToString() != _connectionString || actual.Mode != SqliteOpenMode.Memory)
            throw new InvalidOperationException("Classroom runner may only seed its own in-memory SQLite database.");
        await db.Database.MigrateAsync();
        await SchoolData.SeedAsync(db, Guid.NewGuid(), password);
        var clinic = await db.Clinics.SingleAsync();
        clinic.Name = "Дархан-Уул • Suvd Dent";
        var staff = await db.Users.Where(user => user.Role == Role.Doctor || user.Role == Role.Director).ToListAsync();
        foreach (var user in staff)
        {
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
            user.FullName = user.Role == Role.Doctor ? "Сургалтын эмч" : "Сургалтын зохицуулагч";
        }
        var doctor = await db.DoctorProfiles.SingleAsync();
        doctor.Specialty = "Шүдний эмчилгээ";
        doctor.Biography = "Шүдний үзлэг, цэвэрлэгээ болон нөхөн сэргээх эмчилгээний цаг авна.";
        var exam = await db.Services.SingleAsync();
        exam.Name = "Шүдний үзлэг";
        exam.Description = "Эмч шүд, буйлны байдлыг шалгаж, шаардлагатай дараагийн алхмыг тайлбарлана.";
        exam.MinPrice = 15000;
        exam.MaxPrice = 15000;
        var clean = new Service
        {
            ClinicId = clinic.Id, Name = "Шүдний цэвэрлэгээ",
            Description = "Шүдний өнгөр, чулууг эмнэлэгт мэргэжлийн аргаар цэвэрлэнэ.",
            MinPrice = 35000, MaxPrice = 45000, IsActive = true
        };
        var treatment = new Service
        {
            ClinicId = clinic.Id, Name = "Шүдний эмчилгээ",
            Description = "Үзлэгийн дүнгээр эмчилгээний төлөвлөгөө, боломжит хувилбаруудыг эмчтэй ярилцана.",
            MinPrice = 50000, MaxPrice = 110000, IsActive = true
        };
        db.Services.AddRange(clean, treatment);
        await db.SaveChangesAsync();
        foreach (var service in new[] { clean, treatment })
            db.DoctorServices.Add(new DoctorService
            {
                ClinicId = clinic.Id, DoctorProfileId = doctor.Id, ServiceId = service.Id,
                DurationMinutes = 30, IsActive = true
            });
        var patient = await db.Patients.SingleAsync();
        var start = DateTimeOffset.UtcNow.AddDays(-1);
        db.Appointments.Add(new Appointment
        {
            ClinicId = clinic.Id, PatientId = patient.Id, DoctorProfileId = doctor.Id, ServiceId = exam.Id,
            StartUtc = start, EndUtc = start.AddMinutes(30), OccupiedStartUtc = start, OccupiedEndUtc = start.AddMinutes(30),
            DurationMinutesSnapshot = 30, ClinicTimeZoneIdSnapshot = "Asia/Ulaanbaatar",
            RequestId = Guid.NewGuid(), Status = AppointmentStatus.Confirmed, CreatedAtUtc = start.AddDays(-1)
        });
        await db.SaveChangesAsync();
        return clinic.Id;
    }

    private sealed class AcademicEmailSink() : EmailService(new ConfigurationBuilder().Build())
    {
        public override Task SendAsync(string toEmail, string subject, string body,
            string? messageId = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!toEmail.EndsWith("@example.invalid", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Only synthetic @example.invalid contacts are allowed in the classroom.");
            var registration = Regex.Match(body, @"Таны баталгаажуулах код: ([0-9]{6})(?![0-9])");
            if (!subject.Contains("код", StringComparison.OrdinalIgnoreCase) || !registration.Success)
                throw new InvalidOperationException("Classroom runner does not send email.");
            Console.WriteLine($"Classroom verification code: {registration.Groups[1].Value}");
            return Task.CompletedTask;
        }
    }
}
