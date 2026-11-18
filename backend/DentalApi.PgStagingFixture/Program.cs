using System.Security.Cryptography;
using DentalApi.Data;
using DentalApi.Models;
using DentalApi.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DentalApi.PgStagingFixture;

// A single, synthetic, non-production fixture. No schema migration, reset,
// cleanup, external delivery, or arbitrary target support is implemented.
public static class Program
{
    private const string Target = "nexora_restore_check_20261118_201309";
    private const string Owner = "nexora_dev";
    private const string Host = "127.0.0.1";
    private const int Port = 5432;
    private const string Approval = "CREATE_SYNTHETIC_PG_STAGING_FIXTURE";
    private const string Scope = "NEXORA-PG-STAGING-SYNTHETIC-ONLY";
    // SQL identifiers cannot be parameterized. Keep every counted table name
    // and its query as a hard-coded literal, never constructed from input.
    private static readonly (string Name, string CountSql)[] TableCounts =
    [
        ("Appointments", "SELECT COUNT(*) AS \"Value\" FROM public.\"Appointments\""),
        ("ClinicPatientLinks", "SELECT COUNT(*) AS \"Value\" FROM public.\"ClinicPatientLinks\""),
        ("Clinics", "SELECT COUNT(*) AS \"Value\" FROM public.\"Clinics\""),
        ("DoctorProfiles", "SELECT COUNT(*) AS \"Value\" FROM public.\"DoctorProfiles\""),
        ("DoctorServices", "SELECT COUNT(*) AS \"Value\" FROM public.\"DoctorServices\""),
        ("DoctorWorkingHours", "SELECT COUNT(*) AS \"Value\" FROM public.\"DoctorWorkingHours\""),
        ("NotificationOutbox", "SELECT COUNT(*) AS \"Value\" FROM public.\"NotificationOutbox\""),
        ("NotificationPreference", "SELECT COUNT(*) AS \"Value\" FROM public.\"NotificationPreference\""),
        ("PatientAvailabilities", "SELECT COUNT(*) AS \"Value\" FROM public.\"PatientAvailabilities\""),
        ("PatientAvailabilitySchedules", "SELECT COUNT(*) AS \"Value\" FROM public.\"PatientAvailabilitySchedules\""),
        ("Patients", "SELECT COUNT(*) AS \"Value\" FROM public.\"Patients\""),
        ("PhoneVerificationChallenge", "SELECT COUNT(*) AS \"Value\" FROM public.\"PhoneVerificationChallenge\""),
        ("PlatformAccounts", "SELECT COUNT(*) AS \"Value\" FROM public.\"PlatformAccounts\""),
        ("PlatformVerificationCodes", "SELECT COUNT(*) AS \"Value\" FROM public.\"PlatformVerificationCodes\""),
        ("Services", "SELECT COUNT(*) AS \"Value\" FROM public.\"Services\""),
        ("Users", "SELECT COUNT(*) AS \"Value\" FROM public.\"Users\""),
        ("VerificationCodes", "SELECT COUNT(*) AS \"Value\" FROM public.\"VerificationCodes\"")
    ];

    // Static SQL for the same 17 tables, not an interpolated identifier.
    private const string LockAllTablesSql = """
        LOCK TABLE
            public."Appointments",
            public."ClinicPatientLinks",
            public."Clinics",
            public."DoctorProfiles",
            public."DoctorServices",
            public."DoctorWorkingHours",
            public."NotificationOutbox",
            public."NotificationPreference",
            public."PatientAvailabilities",
            public."PatientAvailabilitySchedules",
            public."Patients",
            public."PhoneVerificationChallenge",
            public."PlatformAccounts",
            public."PlatformVerificationCodes",
            public."Services",
            public."Users",
            public."VerificationCodes"
        IN SHARE ROW EXCLUSIVE MODE
        """;

    public static async Task<int> Main(string[] args)
    {
        // Only pre-defined stages and exception categories reach the console.
        // Never emit exception messages, SQL parameters or connection strings.
        var stage = "arguments";
        try
        {
            if (args.Length != 1 || args[0] is not ("plan" or "create" or "inspect"))
                throw new InvalidOperationException("Usage: plan|create|inspect; no target arguments accepted.");
            var mode = args[0];
            stage = "target configuration";
            // The database target is fixed in source code. Accept only the
            // private password, not a user-assembled connection string. This
            // safely handles special characters including semicolons and quotes.
            var dbPassword = Environment.GetEnvironmentVariable("NEXORA_PG_STAGING_DB_PASSWORD");
            if (string.IsNullOrEmpty(dbPassword))
                throw new InvalidOperationException(
                    "NEXORA_PG_STAGING_DB_PASSWORD is required (private process environment).");
            var connection = new NpgsqlConnectionStringBuilder
            {
                Host = Host,
                Port = Port,
                Database = Target,
                Username = Owner,
                Password = dbPassword,
                SearchPath = "public",
                Pooling = false,
                Enlist = false,
                LogParameters = false,
                IncludeErrorDetail = false,
                Multiplexing = false,
                ApplicationName = "NexoraSyntheticPgStaging"
            };
            stage = "provider setup";
            await using var db = new PostgreSqlAppDbContext(
                new DbContextOptionsBuilder<PostgreSqlAppDbContext>()
                    .UseNpgsql(connection.ConnectionString).Options);

            // Use read-only preflight even for create. Prevents writes until every
            // identity/schema/emptiness condition is satisfied.
            stage = "read-only connection";
            await using (var preflight = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead))
            {
                stage = "read-only transaction";
                await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY");
                stage = "database identity";
                var current = await db.Database.SqlQueryRaw<string>(
                    "SELECT current_database() AS \"Value\"").SingleAsync();
                var username = await db.Database.SqlQueryRaw<string>(
                    "SELECT current_user AS \"Value\"").SingleAsync();
                if (current != Target || username != Owner)
                    throw new InvalidOperationException("Database identity mismatch. No fixture writes permitted.");

                stage = "migration history";
                await DatabaseProviderSettings.VerifyPostgreSqlSchemaAsync(db);
                stage = "table inventory";
                var existingTables = await db.Database.SqlQueryRaw<string>(
                    "SELECT tablename AS \"Value\" FROM pg_catalog.pg_tables WHERE schemaname = 'public' AND tablename <> '__EFMigrationsHistory' ORDER BY tablename")
                    .ToListAsync();
                if (!existingTables.OrderBy(x => x, StringComparer.Ordinal)
                        .SequenceEqual(TableCounts.Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal))
                    throw new InvalidOperationException("Unexpected staging table inventory. No fixture writes permitted.");

                stage = "table row counts";
                // Only use a fixed, allowlisted set of quoted table names.
                var counts = new List<(string Table, long Count)>();
                foreach (var table in TableCounts)
                {
                    var count = await db.Database.SqlQueryRaw<long>(table.CountSql).SingleAsync();
                    counts.Add((table.Name, count));
                }
                var isEmpty = counts.All(item => item.Count == 0);
                Console.WriteLine($"Target verified: {Target}; provider: PostgreSQL; migrations: exact match; tables: {counts.Count}");
                Console.WriteLine($"All fixture tables empty: {isEmpty}");
                if (mode == "plan" || mode == "create")
                {
                    if (!isEmpty)
                        throw new InvalidOperationException("Staging database contains data. Creation refused; nothing will be replaced.");
                }
                else
                {
                    foreach (var item in counts)
                        Console.WriteLine($"{item.Table}: {item.Count}");
                }
                stage = "read-only completion";
                await preflight.CommitAsync();
            }

            if (mode != "create")
            {
                Console.WriteLine("READ-ONLY CHECK COMPLETE. No fixture data created.");
                return 0;
            }

            stage = "write authorization";
            if (Environment.GetEnvironmentVariable("NEXORA_PG_STAGING_WRITE_OPT_IN") != Approval)
                throw new InvalidOperationException("Explicit synthetic fixture write opt-in is missing.");
            var password = Environment.GetEnvironmentVariable("NEXORA_PG_STAGING_PATIENT_PASSWORD");
            if (string.IsNullOrEmpty(password) || password.Length < 8 ||
                System.Text.Encoding.UTF8.GetByteCount(password) > 72)
                throw new InvalidOperationException("Private synthetic patient password must contain 8-72 UTF-8 bytes.");

            stage = "write transaction";
            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            // Repeat the empty-database check within the write transaction.
            // Lock known application tables to exclude competing inserts until commit.
            // Fail promptly instead of waiting indefinitely for another session.
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s'");
            stage = "table locking";
            await db.Database.ExecuteSqlRawAsync(LockAllTablesSql);
            foreach (var table in TableCounts)
            {
                if (await db.Database.SqlQueryRaw<long>(table.CountSql).SingleAsync() != 0)
                    throw new InvalidOperationException("Staging data appeared during preflight. Transaction aborted.");
            }

            stage = "synthetic data insert";
            var marker = Scope + "/" + Guid.NewGuid().ToString("N");
            var clinic = new Clinic
            {
                Name = marker, WorkHours = "SYNTHETIC ONLY: 09:00-17:00",
                CountryCode = "MN", ProvinceOrCapitalCode = "11",
                SoumOrDistrictCode = "1101",
                AddressLine = "SYNTHETIC TEST LOCATION - NOT A REAL CLINIC"
            };
            db.Clinics.Add(clinic);
            await db.SaveChangesAsync();

            User MakeUser(Role role, string suffix, string hash) => new()
            {
                ClinicId = clinic.Id, FullName = "SYNTHETIC " + suffix,
                Email = suffix.ToLowerInvariant() + "@example.invalid",
                PasswordHash = hash, Role = role,
                IsActive = true, IsEmailVerified = true,
                CreatedAt = DateTime.UtcNow
            };

            var director = MakeUser(Role.Director, "Director-" + Guid.NewGuid().ToString("N"), BCrypt.Net.BCrypt.HashPassword(Convert.ToBase64String(RandomNumberGenerator.GetBytes(24))));
            var doctor = MakeUser(Role.Doctor, "Doctor-" + Guid.NewGuid().ToString("N"), BCrypt.Net.BCrypt.HashPassword(Convert.ToBase64String(RandomNumberGenerator.GetBytes(24))));
            var patient = MakeUser(Role.Patient, "Patient-" + Guid.NewGuid().ToString("N"), BCrypt.Net.BCrypt.HashPassword(password));
            db.Users.AddRange(director, doctor, patient);
            await db.SaveChangesAsync();

            clinic.TimeZoneId = "Asia/Ulaanbaatar";
            clinic.TimeZoneStatus = TimeZoneStatus.Verified;
            clinic.TimeZoneVerifiedAtUtc = DateTime.UtcNow;
            clinic.TimeZoneVerifiedByUserId = director.Id;
            clinic.TimeZoneVerifiedClinicId = clinic.Id;
            var doctorProfile = new DoctorProfile
            {
                ClinicId = clinic.Id, UserId = doctor.Id,
                Specialty = "SYNTHETIC DENTAL TEST", PracticeStartYear = 2020,
                Biography = "Disposable staging demonstration only.",
                IsPublished = true, IsActive = true
            };
            var service = new Service
            {
                ClinicId = clinic.Id, Name = "SYNTHETIC TEST SERVICE",
                Description = "Not a clinical treatment offering.",
                MinPrice = 0, MaxPrice = 0, IsActive = true
            };
            db.DoctorProfiles.Add(doctorProfile);
            db.Services.Add(service);
            db.Patients.Add(new Patient
            {
                ClinicId = clinic.Id, UserId = patient.Id,
                FullName = "SYNTHETIC TEST PATIENT",
                Phone = "00000000", IsActive = true
            });
            await db.SaveChangesAsync();

            db.DoctorServices.Add(new DoctorService
            {
                ClinicId = clinic.Id, DoctorProfileId = doctorProfile.Id,
                ServiceId = service.Id, DurationMinutes = 30,
                BufferBeforeMinutes = 0, BufferAfterMinutes = 0, IsActive = true
            });
            for (var day = 0; day < 7; day++)
                db.DoctorWorkingHours.Add(new DoctorWorkingHours
                {
                    ClinicId = clinic.Id, DoctorProfileId = doctorProfile.Id,
                    DayOfWeek = day, StartMinute = 540, EndMinute = 1020
                });
            await db.SaveChangesAsync();

            stage = "clinic timezone policy";
            var policy = await new ClinicSchedulingPolicy(db, new MongoliaLocations())
                .RequireVerifiedAsync(clinic.Id);
            if (policy.TimeZone is null)
                throw new InvalidOperationException("Synthetic clinic timezone verification failed.");
            stage = "commit synthetic fixture";
            await tx.CommitAsync();
            Console.WriteLine("SYNTHETIC STAGING FIXTURE CREATED.");
            Console.WriteLine($"Clinic ID: {clinic.Id}");
            Console.WriteLine($"Patient login: {patient.Email}");
            Console.WriteLine("Password: private input (never printed).");
            Console.WriteLine($"Marker: {marker}");
            Console.WriteLine("No production data, provider calls or migrations were used.");
            return 0;
        }
        catch (Exception ex)
        {
            // Never print exception messages or stack traces. These can contain
            // connection strings, parameter values or fixture data.
            var category = ex switch
            {
                PostgresException pg => "PostgreSQL SQLSTATE " + pg.SqlState,
                NpgsqlException => "PostgreSQL connection/transport",
                InvalidOperationException => "failed invariant",
                ArgumentException => "invalid configuration",
                _ => "unexpected error"
            };
            Console.Error.WriteLine($"STAGING FIXTURE FAILED at [{stage}]; category: {category}.");
            return 1;
        }
    }
}
