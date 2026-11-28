using System.Data;
using System.Text;
using DentalApi.Data;
using DentalApi.Models;
using DentalApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;

namespace DentalApi.PgDevelopmentFixture;

internal static class Program
{
    public static Task<int> Main(string[] args) => PgDevelopmentFixture.RunAsync(args);
}

internal static class PgDevelopmentFixture
{
    internal const string Target = "nexora_dental_dev";
    internal const string Owner = "nexora_dev";
    internal const string Host = "127.0.0.1";
    internal const int Port = 5432;
    internal const int ExpectedPostgreSqlMajorVersion = 18;
    internal const int ExpectedMigrationCount = 7;
    internal const string WriteApproval = "CREATE_SYNTHETIC_PG_DEVELOPMENT_FIXTURE";
    internal const string DatabasePasswordVariable = "NEXORA_PG_DEVELOPMENT_DB_PASSWORD";
    internal const string WriteApprovalVariable = "NEXORA_PG_DEVELOPMENT_WRITE_OPT_IN";
    internal const string AccountPasswordVariable = "NEXORA_PG_DEVELOPMENT_ACCOUNT_PASSWORD";
    internal const string TargetIdentitySql = """
        SELECT current_database() || '|' || current_user || '|' ||
            COALESCE(host(inet_server_addr()), '') || '|' ||
            current_setting('port') || '|' || current_schema() AS "Value"
        """;

    // Keep SQL identifiers static and allowlisted. The migration-history table is
    // validated separately and is intentionally not an application-data table.
    internal static readonly (string Name, string CountSql)[] TableCounts =
    [
        ("AppointmentReschedules", "SELECT COUNT(*) AS \"Value\" FROM public.\"AppointmentReschedules\""),
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

    private const string LockAllTablesSql = """
        LOCK TABLE
            public."Appointments",
            public."AppointmentReschedules",
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
            public."VerificationCodes",
            public."__EFMigrationsHistory"
        IN SHARE ROW EXCLUSIVE MODE
        """;

    internal static string BuildConnectionString(string password)
    {
        if (string.IsNullOrEmpty(password))
            throw new InvalidOperationException("Private PostgreSQL development password is required.");

        return new NpgsqlConnectionStringBuilder
        {
            Host = Host,
            Port = Port,
            Database = Target,
            Username = Owner,
            Password = password,
            SearchPath = "public",
            Pooling = false,
            Enlist = false,
            LogParameters = false,
            IncludeErrorDetail = false,
            Multiplexing = false,
            Timeout = 5,
            CommandTimeout = 15,
            ApplicationName = "SuvdDentSyntheticPgDevelopment"
        }.ConnectionString;
    }

    internal static void ValidateAccountPassword(string password)
    {
        var bytes = Encoding.UTF8.GetByteCount(password);
        if (bytes < 12 || bytes > 72)
            throw new InvalidOperationException(
                "Synthetic account password must contain 12-72 UTF-8 bytes.");
    }

    internal static async Task<int> RunAsync(string[] args)
    {
        var stage = "arguments";
        try
        {
            if (args.Length != 1 || args[0] is not ("plan" or "create" or "inspect"))
                throw new InvalidOperationException("Usage: plan|create|inspect; no target arguments accepted.");
            var mode = args[0];

            stage = "private database credential";
            var dbPassword = Environment.GetEnvironmentVariable(DatabasePasswordVariable);
            var connectionString = BuildConnectionString(dbPassword ?? "");
            await using var db = new PostgreSqlAppDbContext(
                new DbContextOptionsBuilder<PostgreSqlAppDbContext>()
                    .UseNpgsql(connectionString).Options);

            stage = "read-only preflight";
            (string Table, long Count)[] counts;
            await using (var preflight = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead))
            {
                await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY");
                stage = "target identity";
                await VerifyTargetAsync(db);
                stage = "migration history";
                await VerifyMigrationHistoryAsync(db);
                stage = "table inventory";
                await VerifyTableInventoryAsync(db);
                stage = "column inventory";
                await VerifyColumnInventoryAsync(db);
                stage = "emptiness check";
                counts = await ReadTableCountsAsync(db);
                PrintPreflight(mode, counts);
                if (mode != "inspect" && counts.Any(row => row.Count != 0))
                    throw new InvalidOperationException(
                        "Development database contains application data. Creation refused; nothing was replaced.");
                await preflight.CommitAsync();
            }

            if (mode != "create")
            {
                Console.WriteLine("READ-ONLY CHECK COMPLETE. No fixture data created.");
                return 0;
            }

            stage = "write authorization";
            if (!string.Equals(Environment.GetEnvironmentVariable(WriteApprovalVariable),
                    WriteApproval, StringComparison.Ordinal))
                throw new InvalidOperationException("Explicit synthetic development write approval is missing.");
            var accountPassword = Environment.GetEnvironmentVariable(AccountPasswordVariable);
            if (string.IsNullOrEmpty(accountPassword))
                throw new InvalidOperationException("Private synthetic account password is required.");
            ValidateAccountPassword(accountPassword);

            stage = "write transaction";
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s'");
            stage = "table locking";
            await db.Database.ExecuteSqlRawAsync(LockAllTablesSql);
            stage = "write target identity";
            await VerifyTargetAsync(db);
            stage = "write migration history";
            await VerifyMigrationHistoryAsync(db);
            stage = "write table inventory";
            await VerifyTableInventoryAsync(db);
            stage = "write column inventory";
            await VerifyColumnInventoryAsync(db);
            stage = "write emptiness check";
            counts = await ReadTableCountsAsync(db);
            if (counts.Any(row => row.Count != 0))
                throw new InvalidOperationException(
                    "Application data appeared after preflight. Transaction aborted without fixture inserts.");

            stage = "synthetic data insert";
            var clinicId = await DevelopmentSeed.InsertAsync(db, accountPassword);
            stage = "commit synthetic fixture";
            await transaction.CommitAsync();

            Console.WriteLine("SYNTHETIC DEVELOPMENT FIXTURE CREATED.");
            Console.WriteLine($"Clinic ID: {clinicId}");
            Console.WriteLine("Synthetic logins:");
            Console.WriteLine("  development.director@example.invalid");
            Console.WriteLine("  development.doctor01@example.invalid");
            Console.WriteLine("  development.doctor02@example.invalid");
            Console.WriteLine("  development.doctor03@example.invalid");
            Console.WriteLine("  development.patient01@example.invalid through development.patient05@example.invalid");
            Console.WriteLine("Account password: private input (never printed).");
            Console.WriteLine("No migrations, deletion, SMS, email, or external delivery were used.");
            return 0;
        }
        catch (Exception ex)
        {
            var category = ex switch
            {
                PostgresException pg => "PostgreSQL SQLSTATE " + pg.SqlState,
                NpgsqlException => "PostgreSQL connection/transport",
                InvalidOperationException => "failed invariant",
                ArgumentException => "invalid configuration",
                _ => "unexpected error"
            };
            Console.Error.WriteLine($"DEVELOPMENT FIXTURE FAILED at [{stage}]; category: {category}.");
            return 1;
        }
    }

    internal static async Task<(string Table, long Count)[]> ReadTableCountsAsync(AppDbContext db)
    {
        var result = new (string Table, long Count)[TableCounts.Length];
        for (var index = 0; index < TableCounts.Length; index++)
        {
            var table = TableCounts[index];
            var count = await db.Database.SqlQueryRaw<long>(table.CountSql).SingleAsync();
            result[index] = (table.Name, count);
        }
        return result;
    }

    internal static async Task EnsureApplicationTablesEmptyAsync(AppDbContext db)
    {
        if (await db.Appointments.AnyAsync() ||
            await db.AppointmentReschedules.AnyAsync() ||
            await db.Set<ClinicPatientLink>().AnyAsync() ||
            await db.Clinics.AnyAsync() ||
            await db.DoctorProfiles.AnyAsync() ||
            await db.DoctorServices.AnyAsync() ||
            await db.DoctorWorkingHours.AnyAsync() ||
            await db.Set<NotificationOutbox>().AnyAsync() ||
            await db.Set<NotificationPreference>().AnyAsync() ||
            await db.PatientAvailabilities.AnyAsync() ||
            await db.PatientAvailabilitySchedules.AnyAsync() ||
            await db.Patients.AnyAsync() ||
            await db.Set<PhoneVerificationChallenge>().AnyAsync() ||
            await db.PlatformAccounts.AnyAsync() ||
            await db.PlatformVerificationCodes.AnyAsync() ||
            await db.Services.AnyAsync() ||
            await db.Users.AnyAsync() ||
            await db.VerificationCodes.AnyAsync())
            throw new InvalidOperationException(
                "Application tables are not empty. Fixture creation is refused.");
    }

    private static void PrintPreflight(string mode, IReadOnlyList<(string Table, long Count)> counts)
    {
        Console.WriteLine(
            $"Target verified: {Target}; PostgreSQL {ExpectedPostgreSqlMajorVersion}; " +
            $"migrations: {ExpectedMigrationCount}; application tables: {counts.Count}");
        Console.WriteLine($"All application tables empty: {counts.All(row => row.Count == 0)}");
        if (mode == "plan")
            Console.WriteLine(
                "Planned synthetic rows: 1 clinic, 1 director, 3 doctors, 5 patients, " +
                "6 services, 6 doctor-service links, and 15 doctor working-hour rows.");
        if (mode == "inspect")
            foreach (var row in counts)
                Console.WriteLine($"{row.Table}: {row.Count}");
    }

    private static async Task VerifyTargetAsync(PostgreSqlAppDbContext db)
    {
        var identity = await db.Database.SqlQueryRaw<string>(
            TargetIdentitySql).SingleAsync();
        if (!HasApprovedTargetIdentity(identity))
            throw new InvalidOperationException("Database identity mismatch. No fixture writes permitted.");

        var majorVersion = await db.Database.SqlQueryRaw<int>(
            "SELECT current_setting('server_version_num')::integer / 10000 AS \"Value\"").SingleAsync();
        if (!HasApprovedPostgreSqlVersion(majorVersion))
            throw new InvalidOperationException("PostgreSQL server version mismatch. No fixture writes permitted.");
    }

    internal static bool HasApprovedTargetIdentity(string identity) =>
        identity == $"{Target}|{Owner}|{Host}|{Port}|public";

    internal static bool HasApprovedPostgreSqlVersion(int majorVersion) =>
        majorVersion == ExpectedPostgreSqlMajorVersion;

    private static async Task VerifyMigrationHistoryAsync(PostgreSqlAppDbContext db)
    {
        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        var expected = db.Database.GetMigrations().ToArray();
        DatabaseProviderSettings.VerifyPostgreSqlMigrationHistory(expected, applied);
        if (expected.Length != ExpectedMigrationCount)
            throw new InvalidOperationException("Unexpected PostgreSQL migration set. No fixture writes permitted.");
    }

    private static async Task VerifyTableInventoryAsync(PostgreSqlAppDbContext db)
    {
        var modelTables = db.Model.GetRelationalModel().Tables
            .Select(table => (Schema: table.Schema ?? "public", table.Name))
            .Where(table => table.Schema == "public")
            .Select(table => table.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var allowlistedTables = TableCounts.Select(table => table.Name)
            .OrderBy(name => name, StringComparer.Ordinal).ToArray();
        if (!modelTables.SequenceEqual(allowlistedTables, StringComparer.Ordinal))
            throw new InvalidOperationException("Fixture table allowlist differs from the EF model.");

        var existingTables = await db.Database.SqlQueryRaw<string>(
                """
                SELECT tablename AS "Value"
                FROM pg_catalog.pg_tables
                WHERE schemaname = 'public'
                ORDER BY tablename
                """)
            .ToArrayAsync();
        Array.Sort(existingTables, StringComparer.Ordinal);
        var expectedTables = allowlistedTables.Append("__EFMigrationsHistory")
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (!existingTables.SequenceEqual(expectedTables, StringComparer.Ordinal))
            throw new InvalidOperationException("Unexpected development table inventory.");
    }

    private static async Task VerifyColumnInventoryAsync(PostgreSqlAppDbContext db)
    {
        var actualColumns = await db.Database.SqlQueryRaw<string>(
                """
                SELECT table_name || '.' || column_name AS "Value"
                FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name <> '__EFMigrationsHistory'
                ORDER BY table_name, column_name
                """)
            .ToArrayAsync();
        Array.Sort(actualColumns, StringComparer.Ordinal);
        var expectedColumns = db.Model.GetRelationalModel().Tables
            .Where(table => (table.Schema ?? "public") == "public")
            .SelectMany(table => table.Columns.Select(column => table.Name + "." + column.Name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (!actualColumns.SequenceEqual(expectedColumns, StringComparer.Ordinal))
            throw new InvalidOperationException("Development database columns differ from the EF model.");

        var migrationHistoryColumns = await db.Database.SqlQueryRaw<string>(
                """
                SELECT column_name AS "Value"
                FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = '__EFMigrationsHistory'
                ORDER BY ordinal_position
                """)
            .ToArrayAsync();
        if (!migrationHistoryColumns.SequenceEqual(
                ["MigrationId", "ProductVersion"], StringComparer.Ordinal))
            throw new InvalidOperationException("Unexpected EF migration-history table schema.");
    }
}

internal static class DevelopmentSeed
{
    private static readonly string[] ServiceNames =
    [
        "SYNTHETIC - Consultation",
        "SYNTHETIC - Cleaning",
        "SYNTHETIC - Filling",
        "SYNTHETIC - Extraction",
        "SYNTHETIC - X-ray placeholder",
        "SYNTHETIC - Follow-up"
    ];

    internal static async Task<int> InsertAsync(AppDbContext db, string accountPassword)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Synthetic fixture inserts require the caller's atomic transaction.");
        PgDevelopmentFixture.ValidateAccountPassword(accountPassword);
        await PgDevelopmentFixture.EnsureApplicationTablesEmptyAsync(db);

        var now = DateTime.UtcNow;
        var clinic = new Clinic
        {
            Name = "SYNTHETIC DEVELOPMENT FIXTURE - NOT A REAL CLINIC",
            WorkHours = "SYNTHETIC ONLY: 09:00-17:00",
            CountryCode = "MN",
            ProvinceOrCapitalCode = "11",
            SoumOrDistrictCode = "1101",
            AddressLine = "SYNTHETIC DEVELOPMENT LOCATION - NOT A REAL CLINIC"
        };
        db.Clinics.Add(clinic);
        await db.SaveChangesAsync();

        User NewUser(string name, string email, Role role) => new()
        {
            ClinicId = clinic.Id,
            FullName = name,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(accountPassword),
            Role = role,
            IsActive = true,
            IsEmailVerified = true,
            CreatedAt = now
        };

        var director = NewUser(
            "Synthetic development director",
            "development.director@example.invalid",
            Role.Director);
        var doctors = Enumerable.Range(1, 3)
            .Select(index => NewUser(
                $"Synthetic development doctor {index:00}",
                $"development.doctor{index:00}@example.invalid",
                Role.Doctor))
            .ToArray();
        var patientUsers = Enumerable.Range(1, 5)
            .Select(index => NewUser(
                $"Synthetic development patient {index:00}",
                $"development.patient{index:00}@example.invalid",
                Role.Patient))
            .ToArray();
        db.Users.AddRange([director, .. doctors, .. patientUsers]);
        await db.SaveChangesAsync();

        clinic.TimeZoneId = "Asia/Ulaanbaatar";
        clinic.TimeZoneStatus = TimeZoneStatus.Verified;
        clinic.TimeZoneVerifiedAtUtc = now;
        clinic.TimeZoneVerifiedByUserId = director.Id;
        clinic.TimeZoneVerifiedClinicId = clinic.Id;

        var profiles = doctors.Select((doctor, index) => new DoctorProfile
        {
            ClinicId = clinic.Id,
            UserId = doctor.Id,
            Specialty = "SYNTHETIC DEVELOPMENT ONLY",
            PracticeStartYear = 2020,
            Biography = "Non-clinical synthetic development fixture only.",
            IsPublished = true,
            IsActive = true
        }).ToArray();
        var services = ServiceNames.Select(name => new Service
        {
            ClinicId = clinic.Id,
            Name = name,
            Description = "Synthetic placeholder only; not a real clinical offering.",
            MinPrice = 0,
            MaxPrice = 0,
            IsActive = true
        }).ToArray();
        var patients = patientUsers.Select((user, index) => new Patient
        {
            ClinicId = clinic.Id,
            UserId = user.Id,
            FullName = user.FullName,
            Phone = $"0000000{index + 1}",
            CreatedAt = now,
            IsActive = true
        }).ToArray();

        db.DoctorProfiles.AddRange(profiles);
        db.Services.AddRange(services);
        db.Patients.AddRange(patients);
        await db.SaveChangesAsync();

        var doctorServices = profiles.SelectMany((profile, doctorIndex) =>
            Enumerable.Range(doctorIndex * 2, 2).Select(serviceIndex => new DoctorService
            {
                ClinicId = clinic.Id,
                DoctorProfileId = profile.Id,
                ServiceId = services[serviceIndex].Id,
                DurationMinutes = 30,
                BufferBeforeMinutes = 0,
                BufferAfterMinutes = 0,
                IsActive = true
            }));
        db.DoctorServices.AddRange(doctorServices);
        db.DoctorWorkingHours.AddRange(profiles.SelectMany(profile =>
            Enumerable.Range(1, 5).Select(day => new DoctorWorkingHours
            {
                ClinicId = clinic.Id,
                DoctorProfileId = profile.Id,
                DayOfWeek = day,
                StartMinute = 540,
                EndMinute = 1020
            })));
        await db.SaveChangesAsync();

        var scheduling = await new ClinicSchedulingPolicy(db, new MongoliaLocations())
            .RequireVerifiedAsync(clinic.Id);
        if (scheduling.TimeZone is null)
            throw new InvalidOperationException("Synthetic clinic timezone verification failed.");
        return clinic.Id;
    }
}
