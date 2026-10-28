using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DentalApi.Data;
using DentalApi.Models;
using DentalApi.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace DentalApi.SchoolFixture;

public static class FixtureSafety
{
    public const string OptInVariable = "NEXORA_SCHOOL_FIXTURE_WRITE_OPT_IN";
    public const string OptInValue = "CREATE_NEW_SQLITE_SCHOOL_FIXTURE";
    public const string PasswordVariable = "NEXORA_SCHOOL_PATIENT_PASSWORD";
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexoraDental", "school-test");
    public static string Resolve(string path, string? root = null)
    {
        if (!Path.IsPathFullyQualified(path)) throw new InvalidOperationException("An absolute database FILE PATH is required.");
        var full = Path.GetFullPath(path);
        var expectedRoot = Path.GetFullPath(root ?? Root).TrimEnd(Path.DirectorySeparatorChar);
        if (!string.Equals(Path.GetDirectoryName(full), expectedRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
            !Path.GetFileName(full).StartsWith("step5-", StringComparison.Ordinal) || Path.GetExtension(full) != ".sqlite" ||
            Path.GetFileName(full).Contains(':'))
            throw new InvalidOperationException("Use a direct step5-*.sqlite child of the dedicated NexoraDental school-test directory.");
        for (var directory = new DirectoryInfo(expectedRoot); directory != null; directory = directory.Parent)
            if (directory.LinkTarget != null || (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0))
                throw new InvalidOperationException("Linked/junction directories are not permitted.");
        return full;
    }
    public static bool Exists(string path) => File.Exists(path) || Directory.Exists(path) || new FileInfo(path).LinkTarget != null;
    public static void RequireNew(string path)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm", "-journal", ".fixture.json" })
            if (Exists(path + suffix)) throw new InvalidOperationException("Target or SQLite/fixture sidecar already exists. Nothing may be replaced; choose a new name.");
    }
    public static void RequireOptIn(string? value)
    {
        if (value != OptInValue) throw new InvalidOperationException("Explicit fixture write opt-in is missing. No database writes permitted.");
    }
    public static void RequirePassword(string? password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8 || Encoding.UTF8.GetByteCount(password) > 72)
            throw new InvalidOperationException("Provide the private patient password via the private prompt/environment (8+ characters, at most 72 UTF-8 bytes).");
    }
    public static void RequireSafeStartup(IConfiguration config, string path)
    {
        if (DatabaseProviderSettings.Selected(config) != DatabaseProviderSettings.Sqlite) throw new InvalidOperationException("Only SQLite startup is permitted.");
        StartupConfigurationValidator.Validate(config, "Development");
        var connection = new SqliteConnectionStringBuilder(DatabaseProviderSettings.ConnectionString(config));
        if (connection.DataSource != path || connection.Mode != SqliteOpenMode.ReadWrite || connection.Pooling || connection.ForeignKeys != true)
            throw new InvalidOperationException("Startup must target this exact existing SQLite file in ReadWrite/no-pooling/foreign-keys mode.");
        if (!StartupConfigurationValidator.AllowedOrigins(config).SequenceEqual(new[] { "http://localhost:7357" }) ||
            config.GetSection("Kestrel:Endpoints").GetChildren().Any() || config.GetValue<bool>("ForwardedHeaders:Enabled") ||
            config["urls"] != "http://127.0.0.1:5018" || config["AllowedHosts"] != "127.0.0.1;localhost" ||
            !string.IsNullOrEmpty(config["HTTPS_PORT"]) || config.GetSection("HttpsRedirection").GetChildren().Any())
            throw new InvalidOperationException("Startup requires loopback-only binding, no inherited endpoints/HTTPS redirects and exactly the school Flutter origin. Resolve inherited conflicting settings privately.");
    }
    public static DbContextOptions<AppDbContext> Options(string path, bool readOnly) => new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite(new SqliteConnectionStringBuilder { DataSource = path, Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWrite, Pooling = false, ForeignKeys = true }.ToString()).Options;
}

public record FixtureOwner(string Scope, Guid RunId, string DatabasePath);
public record FixtureInfo(string DatabasePath, int ClinicId, int DoctorProfileId, int ServiceId, int PatientId, string SuggestedDate, int AppointmentCount);

public static class SchoolData
{
    public const string Scope = "NEXORA-SCHOOL-STEP5-SYNTHETIC-ONLY";
    public const string PatientEmail = "school-patient@example.invalid";
    public static async Task SeedAsync(AppDbContext db, Guid runId, string password)
    {
        FixtureSafety.RequirePassword(password);
        if (!db.Database.IsSqlite()) throw new InvalidOperationException("Only SQLite is permitted.");
        if (await db.Clinics.AnyAsync() || await db.Users.AnyAsync() || await db.Patients.AnyAsync() ||
            await db.Services.AnyAsync() || await db.DoctorProfiles.AnyAsync() || await db.Appointments.AnyAsync())
            throw new InvalidOperationException("Fixture requires a new empty database; existing records are never replaced.");
        await using var tx = await db.Database.BeginTransactionAsync();
        var clinic = new Clinic { Name = $"{Scope}/{runId:D}", WorkHours = "SYNTHETIC: daily 09:00-17:00", CountryCode = "MN", ProvinceOrCapitalCode = "11", SoumOrDistrictCode = "1101", AddressLine = "SYNTHETIC SCHOOL TEST ADDRESS - NOT A REAL CLINIC" };
        db.Clinics.Add(clinic); await db.SaveChangesAsync();
        User NewUser(Role role, string email, string hash) => new() { ClinicId = clinic.Id, FullName = $"SYNTHETIC SCHOOL {role}", Email = email, PasswordHash = hash, Role = role, IsActive = true, IsEmailVerified = true, CreatedAt = DateTime.UtcNow };
        var director = NewUser(Role.Director, "school-director@example.invalid", BCrypt.Net.BCrypt.HashPassword(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))));
        var doctor = NewUser(Role.Doctor, "school-doctor@example.invalid", BCrypt.Net.BCrypt.HashPassword(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))));
        var patientUser = NewUser(Role.Patient, PatientEmail, BCrypt.Net.BCrypt.HashPassword(password));
        db.Users.AddRange(director, doctor, patientUser); await db.SaveChangesAsync();
        clinic.TimeZoneStatus = TimeZoneStatus.Verified; clinic.TimeZoneId = "Asia/Ulaanbaatar";
        clinic.TimeZoneVerifiedAtUtc = DateTime.UtcNow; clinic.TimeZoneVerifiedByUserId = director.Id; clinic.TimeZoneVerifiedClinicId = clinic.Id;
        var profile = new DoctorProfile { ClinicId = clinic.Id, UserId = doctor.Id, Specialty = "SYNTHETIC SCHOOL DENTISTRY", PracticeStartYear = 2020, Biography = "Disposable test fixture only.", IsActive = true, IsPublished = true };
        var service = new Service { ClinicId = clinic.Id, Name = "SYNTHETIC SCHOOL TREATMENT", Description = "Disposable appointment rehearsal only.", MinPrice = 0, MaxPrice = 0, IsActive = true };
        db.DoctorProfiles.Add(profile); db.Services.Add(service);
        db.Patients.Add(new Patient { ClinicId = clinic.Id, UserId = patientUser.Id, FullName = "SYNTHETIC SCHOOL PATIENT", Phone = "00000000", Address = "SYNTHETIC SCHOOL TEST ADDRESS", IsActive = true });
        await db.SaveChangesAsync();
        db.DoctorServices.Add(new DoctorService { ClinicId = clinic.Id, DoctorProfileId = profile.Id, ServiceId = service.Id, DurationMinutes = 30, BufferBeforeMinutes = 0, BufferAfterMinutes = 0, IsActive = true });
        for (var day = 0; day < 7; day++) db.DoctorWorkingHours.Add(new DoctorWorkingHours { ClinicId = clinic.Id, DoctorProfileId = profile.Id, DayOfWeek = day, StartMinute = 540, EndMinute = 1020 });
        await db.SaveChangesAsync();
        var policy = await new ClinicSchedulingPolicy(db, new MongoliaLocations()).RequireVerifiedAsync(clinic.Id);
        if (policy.TimeZone == null) throw new InvalidOperationException("Fixture timezone verification failed.");
        await tx.CommitAsync();
    }
    public static async Task<FixtureInfo> InspectAsync(string path)
    {
        path = FixtureSafety.Resolve(path);
        if (!File.Exists(path) || !File.Exists(path + ".fixture.json")) throw new InvalidOperationException("Owned fixture files are missing.");
        foreach (var candidate in new[] { path, path + ".fixture.json", path + "-wal", path + "-shm", path + "-journal" })
            if (FixtureSafety.Exists(candidate) && (File.GetAttributes(candidate) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Linked fixture files are not permitted.");
        var owner = JsonSerializer.Deserialize<FixtureOwner>(await File.ReadAllTextAsync(path + ".fixture.json"));
        if (owner == null || owner.Scope != Scope || owner.RunId == Guid.Empty || owner.DatabasePath != path) throw new InvalidOperationException("Fixture ownership marker is invalid.");
        await using var db = new AppDbContext(FixtureSafety.Options(path, true));
        if ((await db.Database.GetPendingMigrationsAsync()).Any()) throw new InvalidOperationException("Pending migrations found; refuse backend startup against this existing file.");
        var clinic = await db.Clinics.SingleAsync();
        var users = await db.Users.ToArrayAsync();
        var doctor = await db.DoctorProfiles.SingleAsync();
        var service = await db.Services.SingleAsync();
        var patient = await db.Patients.SingleAsync();
        var association = await db.DoctorServices.SingleAsync();
        var patientUser = users.Single(u => u.Role == Role.Patient);
        var director = users.Single(u => u.Role == Role.Director);
        if (clinic.Name != $"{Scope}/{owner.RunId:D}" || users.Length != 3 || users.Any(u => !u.IsActive || u.ClinicId != clinic.Id || u.Email == null || !u.Email.EndsWith("@example.invalid", StringComparison.Ordinal)) ||
            !patientUser.IsEmailVerified || patientUser.Email != PatientEmail || patient.UserId != patientUser.Id || !patient.IsActive || patient.ClinicId != clinic.Id ||
            !doctor.IsActive || !doctor.IsPublished || doctor.ClinicId != clinic.Id || !users.Any(u => u.Id == doctor.UserId && u.Role == Role.Doctor) ||
            !service.IsActive || service.ClinicId != clinic.Id || !association.IsActive || association.ClinicId != clinic.Id || association.ServiceId != service.Id || association.DoctorProfileId != doctor.Id ||
            clinic.TimeZoneStatus != TimeZoneStatus.Verified || clinic.TimeZoneId != "Asia/Ulaanbaatar" || clinic.TimeZoneVerifiedByUserId != director.Id || clinic.TimeZoneVerifiedClinicId != clinic.Id || clinic.TimeZoneVerifiedAtUtc == null ||
            !new MongoliaLocations().Valid(clinic.CountryCode ?? "", clinic.ProvinceOrCapitalCode ?? "", clinic.SoumOrDistrictCode ?? "") || string.IsNullOrWhiteSpace(clinic.AddressLine) ||
            await db.DoctorWorkingHours.CountAsync() != 7 || await db.Appointments.AnyAsync(a => a.ClinicId != clinic.Id || a.PatientId != patient.Id || a.DoctorProfileId != doctor.Id || a.ServiceId != service.Id))
            throw new InvalidOperationException("Database does not match the owned synthetic school fixture.");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(clinic.TimeZoneId)));
        return new(path, clinic.Id, doctor.Id, service.Id, patient.Id, today.AddDays(1).ToString("yyyy-MM-dd"), await db.Appointments.CountAsync());
    }
}

public static class EntryPoint
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length < 2) throw new InvalidOperationException("Usage: plan|create|inspect|check-start <absolute SQLite FILE PATH> [API project directory]");
            var path = FixtureSafety.Resolve(args[1]);
            Console.WriteLine($"Database FILE PATH: {path}");
            if (args[0] is "plan" or "create")
            {
                FixtureSafety.RequireNew(path);
                if (args[0] == "plan") { Console.WriteLine("New target confirmed. PLAN ONLY: no database connection or writes."); return 0; }
                FixtureSafety.RequireOptIn(Environment.GetEnvironmentVariable(FixtureSafety.OptInVariable));
                var password = Environment.GetEnvironmentVariable(FixtureSafety.PasswordVariable);
                FixtureSafety.RequirePassword(password);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                FixtureSafety.Resolve(path); FixtureSafety.RequireNew(path);
                var owner = new FixtureOwner(SchoolData.Scope, Guid.NewGuid(), path);
                // Atomic exclusive reservation. Failures leave owned partial artifacts for review; never delete or retry in place.
                using (var marker = new FileStream(path + ".fixture.json", FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    await JsonSerializer.SerializeAsync(marker, owner); await marker.FlushAsync();
                    using (new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
                    await using var db = new AppDbContext(FixtureSafety.Options(path, false));
                    await db.Database.MigrateAsync(); // AppDbContext owns SQLite migrations only. No PostgreSQL context.
                    await SchoolData.SeedAsync(db, owner.RunId, password!);
                }
                Environment.SetEnvironmentVariable(FixtureSafety.PasswordVariable, null);
                Console.WriteLine(JsonSerializer.Serialize(await SchoolData.InspectAsync(path)));
                return 0;
            }
            if (args[0] is not ("inspect" or "check-start")) throw new InvalidOperationException("Unknown mode.");
            var info = await SchoolData.InspectAsync(path); // SQLite Mode=ReadOnly; never creates files.
            if (args[0] == "check-start")
            {
                FixtureSafety.RequireOptIn(Environment.GetEnvironmentVariable(FixtureSafety.OptInVariable));
                if (args.Length != 3) throw new InvalidOperationException("Supply the API project directory.");
                var config = new ConfigurationBuilder().AddEnvironmentVariables("ASPNETCORE_").AddEnvironmentVariables("DOTNET_").SetBasePath(Path.GetFullPath(args[2]))
                    .AddJsonFile("appsettings.json", optional: false).AddJsonFile("appsettings.Development.json", optional: true)
                    .AddUserSecrets(typeof(AppDbContext).Assembly, optional: true).AddEnvironmentVariables().Build();
                FixtureSafety.RequireSafeStartup(config, path);
                if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") != "Development" || Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") != "Development")
                    throw new InvalidOperationException("Explicit Development environment is required.");
            }
            Console.WriteLine(JsonSerializer.Serialize(info)); return 0;
        }
        catch (InvalidOperationException error) when (error.Source == typeof(EntryPoint).Assembly.GetName().Name)
        {
            Console.Error.WriteLine(error.Message); return 2;
        }
        catch (Exception)
        {
            // Never print provider exceptions, configuration values, hashes or credentials.
            Console.Error.WriteLine("School fixture operation failed. No cleanup/replacement was attempted. Inspect prerequisites privately and use a new target after any partial creation."); return 2;
        }
    }
}
