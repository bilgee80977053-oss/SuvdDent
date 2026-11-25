using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DentalApi.Data;
using DentalApi.Models;
using DentalApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;

namespace DentalApi.PgCrossTenantFixture;

internal static class Program
{
    public static Task<int> Main(string[] args) => PgCrossTenantFixture.RunAsync(args);
}

internal static class PgCrossTenantFixture
{
    internal const string Target = "nexora_dental_dev";
    internal const string Owner = "nexora_dev";
    internal const string Host = "127.0.0.1";
    internal const int Port = 5432;
    internal const int ExpectedPostgreSqlMajorVersion = 18;
    internal const int ExpectedMigrationCount = 5;
    internal const string WriteApproval = "CREATE_SYNTHETIC_PG_CROSS_TENANT_FIXTURE";
    internal const string DatabasePasswordVariable = "NEXORA_PG_CROSS_TENANT_DB_PASSWORD";
    internal const string WriteApprovalVariable = "NEXORA_PG_CROSS_TENANT_WRITE_OPT_IN";
    internal const string AccountPasswordVariable = "NEXORA_PG_CROSS_TENANT_ACCOUNT_PASSWORD";

    internal static readonly (string Name, string CountSql)[] TableCounts =
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

    private const string TargetIdentitySql = """
        SELECT current_database() || '|' || current_user || '|' ||
            COALESCE(host(inet_server_addr()), '') || '|' ||
            current_setting('port') || '|' || current_schema() AS "Value"
        """;

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
            ApplicationName = "SuvdDentSyntheticPgCrossTenant"
        }.ConnectionString;
    }

    internal static void ValidateAccountPassword(string password)
    {
        var bytes = Encoding.UTF8.GetByteCount(password);
        if (bytes is < 12 or > 72)
            throw new InvalidOperationException("Synthetic account password must contain 12-72 UTF-8 bytes.");
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
            var connectionString = BuildConnectionString(
                Environment.GetEnvironmentVariable(DatabasePasswordVariable) ?? "");
            await using var db = new PostgreSqlAppDbContext(
                new DbContextOptionsBuilder<PostgreSqlAppDbContext>()
                    .UseNpgsql(connectionString).Options);

            (string Table, long Count)[] counts;
            (string Table, long Count)[] lockedCounts;
            CrossTenantState state;
            stage = "read-only preflight";
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
                stage = "Clinic 1 baseline and fixture state";
                state = await CrossTenantDevelopmentSeed.InspectAsync(db);
                stage = "table counts";
                counts = await ReadTableCountsAsync(db);
                PrintPreflight(mode, state, counts);
                if (mode == "create" && state.FixtureClinicId.HasValue)
                    throw new InvalidOperationException("Cross-tenant fixture already exists; duplicate creation refused.");
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
                throw new InvalidOperationException("Explicit cross-tenant fixture write approval is missing.");
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
            stage = "write Clinic 1 baseline and fixture state";
            var lockedState = await CrossTenantDevelopmentSeed.InspectAsync(db);
            if (lockedState.FixtureClinicId.HasValue ||
                !state.ClinicOne.IsUnchanged(lockedState.ClinicOne))
                throw new InvalidOperationException("Clinic 1 changed or the fixture appeared after preflight.");
            stage = "write table counts";
            lockedCounts = await ReadTableCountsAsync(db);
            if (!counts.SequenceEqual(lockedCounts))
                throw new InvalidOperationException("Application table counts changed after preflight.");

            stage = "synthetic second-tenant insert";
            var clinicId = await CrossTenantDevelopmentSeed.InsertAsync(
                db, accountPassword, state.ClinicOne);
            stage = "commit cross-tenant fixture";
            await transaction.CommitAsync();

            Console.WriteLine("SYNTHETIC CROSS-TENANT DEVELOPMENT FIXTURE CREATED.");
            Console.WriteLine($"New synthetic clinic ID: {clinicId}");
            Console.WriteLine("Logins: cross-tenant.director@example.invalid,");
            Console.WriteLine("        cross-tenant.doctor@example.invalid,");
            Console.WriteLine("        cross-tenant.patient@example.invalid");
            Console.WriteLine("Account password: private input (never printed).");
            Console.WriteLine("Clinic 1 fingerprint verified unchanged before commit.");
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
            Console.Error.WriteLine($"CROSS-TENANT FIXTURE FAILED at [{stage}]; category: {category}.");
            return 1;
        }
    }

    internal static async Task<(string Table, long Count)[]> ReadTableCountsAsync(AppDbContext db)
    {
        var result = new (string Table, long Count)[TableCounts.Length];
        for (var index = 0; index < TableCounts.Length; index++)
        {
            var table = TableCounts[index];
            result[index] = (table.Name,
                await db.Database.SqlQueryRaw<long>(table.CountSql).SingleAsync());
        }
        return result;
    }

    private static void PrintPreflight(
        string mode, CrossTenantState state, IReadOnlyList<(string Table, long Count)> counts)
    {
        Console.WriteLine(
            $"Target verified: {Target}; PostgreSQL {ExpectedPostgreSqlMajorVersion}; " +
            $"migrations: {ExpectedMigrationCount}; application tables: {counts.Count}");
        Console.WriteLine($"Clinic 1 unchanged-snapshot rows: {state.ClinicOne.Counts["Appointments"]} appointments");
        Console.WriteLine($"Cross-tenant fixture already exists: {state.FixtureClinicId.HasValue}");
        if (mode == "plan")
            Console.WriteLine(
                "Planned additions: 1 synthetic clinic, 1 director, 1 doctor, 1 patient, " +
                "1 service, 1 doctor-service link, and 5 weekday working-hour rows.");
        if (mode == "inspect")
            foreach (var row in counts)
                Console.WriteLine($"{row.Table}: {row.Count}");
    }

    private static async Task VerifyTargetAsync(PostgreSqlAppDbContext db)
    {
        var identity = await db.Database.SqlQueryRaw<string>(TargetIdentitySql).SingleAsync();
        if (identity != $"{Target}|{Owner}|{Host}|{Port}|public")
            throw new InvalidOperationException("Database identity mismatch. No fixture writes permitted.");
        var majorVersion = await db.Database.SqlQueryRaw<int>(
            "SELECT current_setting('server_version_num')::integer / 10000 AS \"Value\"").SingleAsync();
        if (majorVersion != ExpectedPostgreSqlMajorVersion)
            throw new InvalidOperationException("PostgreSQL server version mismatch. No fixture writes permitted.");
    }

    private static async Task VerifyMigrationHistoryAsync(PostgreSqlAppDbContext db)
    {
        var expected = db.Database.GetMigrations().ToArray();
        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
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
            .OrderBy(name => name, StringComparer.Ordinal).ToArray();
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
            .OrderBy(name => name, StringComparer.Ordinal).ToArray();
        if (!actualColumns.SequenceEqual(expectedColumns, StringComparer.Ordinal))
            throw new InvalidOperationException("Development database columns differ from the EF model.");

        var historyColumns = await db.Database.SqlQueryRaw<string>(
                """
                SELECT column_name AS "Value"
                FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = '__EFMigrationsHistory'
                ORDER BY ordinal_position
                """)
            .ToArrayAsync();
        if (!historyColumns.SequenceEqual(["MigrationId", "ProductVersion"], StringComparer.Ordinal))
            throw new InvalidOperationException("Unexpected EF migration-history table schema.");
    }
}

internal sealed record CrossTenantState(int? FixtureClinicId, ClinicOneSnapshot ClinicOne);

internal sealed record ClinicOneSnapshot(string Fingerprint, IReadOnlyDictionary<string, long> Counts)
{
    internal bool IsUnchanged(ClinicOneSnapshot other) =>
        string.Equals(Fingerprint, other.Fingerprint, StringComparison.Ordinal) &&
        Counts.OrderBy(row => row.Key, StringComparer.Ordinal)
            .SequenceEqual(other.Counts.OrderBy(row => row.Key, StringComparer.Ordinal));

    internal static async Task<ClinicOneSnapshot> CaptureAsync(AppDbContext db)
    {
        const int clinicId = 1;
        var clinics = await db.Clinics.AsNoTracking().Where(row => row.Id == clinicId)
            .OrderBy(row => row.Id).ToArrayAsync();
        var users = await db.Users.AsNoTracking().Where(row => row.ClinicId == clinicId)
            .OrderBy(row => row.Id).ToArrayAsync();
        var patients = await db.Patients.AsNoTracking().Where(row => row.ClinicId == clinicId)
            .OrderBy(row => row.Id).ToArrayAsync();
        var services = await db.Services.AsNoTracking().Where(row => row.ClinicId == clinicId)
            .OrderBy(row => row.Id).ToArrayAsync();
        var profiles = await db.DoctorProfiles.AsNoTracking().Where(row => row.ClinicId == clinicId)
            .OrderBy(row => row.Id).ToArrayAsync();
        var doctorServices = await db.DoctorServices.AsNoTracking().Where(row => row.ClinicId == clinicId)
            .OrderBy(row => row.Id).ToArrayAsync();
        var workingHours = await db.DoctorWorkingHours.AsNoTracking().Where(row => row.ClinicId == clinicId)
            .OrderBy(row => row.Id).ToArrayAsync();
        var appointments = await db.Appointments.AsNoTracking().Where(row => row.ClinicId == clinicId)
            .OrderBy(row => row.Id).ToArrayAsync();
        var patientLinks = await db.Set<ClinicPatientLink>().AsNoTracking()
            .Where(row => row.ClinicId == clinicId).OrderBy(row => row.Id).ToArrayAsync();
        var outbox = await db.Set<NotificationOutbox>().AsNoTracking()
            .Where(row => row.ClinicId == clinicId).OrderBy(row => row.Id).ToArrayAsync();
        var preferences = await db.Set<NotificationPreference>().AsNoTracking()
            .Where(row => row.ClinicId == clinicId).OrderBy(row => row.UserId).ToArrayAsync();
        var availabilities = await db.PatientAvailabilities.AsNoTracking()
            .Where(row => row.ClinicId == clinicId).OrderBy(row => row.Id).ToArrayAsync();
        var schedules = await db.PatientAvailabilitySchedules.AsNoTracking()
            .Where(row => row.ClinicId == clinicId)
            .OrderBy(row => row.PatientId).ToArrayAsync();
        var challenges = await db.Set<PhoneVerificationChallenge>().AsNoTracking()
            .Where(row => row.ClinicId == clinicId).OrderBy(row => row.Id).ToArrayAsync();
        var verificationCodes = await db.VerificationCodes.AsNoTracking()
            .Where(row => db.Users.Any(user => user.ClinicId == clinicId && user.Id == row.UserId))
            .OrderBy(row => row.Id).ToArrayAsync();

        var clinicPlatformIds = db.Set<ClinicPatientLink>().AsNoTracking()
            .Where(row => row.ClinicId == clinicId).Select(row => row.PlatformAccountId);
        var platformAccounts = await db.PlatformAccounts.AsNoTracking()
            .Where(row => clinicPlatformIds.Contains(row.Id)).OrderBy(row => row.Id).ToArrayAsync();
        var platformVerifications = await db.PlatformVerificationCodes.AsNoTracking()
            .Where(row => clinicPlatformIds.Contains(row.PlatformAccountId))
            .OrderBy(row => row.Id).ToArrayAsync();

        var snapshot = new
        {
            Clinics = clinics,
            Users = users,
            Patients = patients,
            Services = services,
            Profiles = profiles,
            DoctorServices = doctorServices,
            WorkingHours = workingHours,
            Appointments = appointments,
            PatientLinks = patientLinks,
            Outbox = outbox,
            Preferences = preferences,
            Availabilities = availabilities,
            Schedules = schedules,
            PhoneChallenges = challenges,
            VerificationCodes = verificationCodes,
            PlatformAccounts = platformAccounts,
            PlatformVerifications = platformVerifications
        };
        var counts = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["Clinics"] = clinics.LongLength,
            ["Users"] = users.LongLength,
            ["Patients"] = patients.LongLength,
            ["Services"] = services.LongLength,
            ["DoctorProfiles"] = profiles.LongLength,
            ["DoctorServices"] = doctorServices.LongLength,
            ["DoctorWorkingHours"] = workingHours.LongLength,
            ["Appointments"] = appointments.LongLength,
            ["ClinicPatientLinks"] = patientLinks.LongLength,
            ["NotificationOutbox"] = outbox.LongLength,
            ["NotificationPreference"] = preferences.LongLength,
            ["PatientAvailabilities"] = availabilities.LongLength,
            ["PatientAvailabilitySchedules"] = schedules.LongLength,
            ["PhoneVerificationChallenge"] = challenges.LongLength,
            ["VerificationCodes"] = verificationCodes.LongLength,
            ["PlatformAccounts"] = platformAccounts.LongLength,
            ["PlatformVerificationCodes"] = platformVerifications.LongLength
        };
        var json = JsonSerializer.Serialize(snapshot);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        return new(fingerprint, counts);
    }
}

internal static class CrossTenantDevelopmentSeed
{
    internal const int BaseClinicId = 1;
    internal const string BaseClinicName = "SYNTHETIC DEVELOPMENT FIXTURE - NOT A REAL CLINIC";
    internal const string FixtureClinicName =
        "SYNTHETIC CROSS-TENANT DEVELOPMENT FIXTURE - NOT A REAL CLINIC";

    private static readonly IReadOnlyDictionary<string, long> ExpectedClinicOneCounts =
        new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["Appointments"] = 3,
            ["ClinicPatientLinks"] = 0,
            ["Clinics"] = 1,
            ["DoctorProfiles"] = 3,
            ["DoctorServices"] = 6,
            ["DoctorWorkingHours"] = 15,
            ["NotificationPreference"] = 0,
            ["PatientAvailabilities"] = 0,
            ["PatientAvailabilitySchedules"] = 0,
            ["Patients"] = 5,
            ["PhoneVerificationChallenge"] = 0,
            ["PlatformAccounts"] = 0,
            ["PlatformVerificationCodes"] = 0,
            ["Services"] = 6,
            ["Users"] = 9,
            ["VerificationCodes"] = 0
        };

    internal static async Task<CrossTenantState> InspectAsync(AppDbContext db)
    {
        var fixtureClinics = await db.Clinics.AsNoTracking()
            .Where(clinic => clinic.Name == FixtureClinicName)
            .Select(clinic => clinic.Id).ToArrayAsync();
        if (fixtureClinics.Length > 1)
            throw new InvalidOperationException("Duplicate cross-tenant fixture markers exist.");

        var totalClinics = await db.Clinics.CountAsync();
        var expectedClinics = fixtureClinics.Length == 0 ? 1 : 2;
        if (!await db.Clinics.AnyAsync(clinic => clinic.Id == BaseClinicId) ||
            totalClinics != expectedClinics ||
            (fixtureClinics.Length == 1 && fixtureClinics[0] == BaseClinicId))
            throw new InvalidOperationException("Expected Clinic 1 and no unrelated clinic records.");

        var snapshot = await ClinicOneSnapshot.CaptureAsync(db);
        int? fixtureId = fixtureClinics.Length == 0 ? null : fixtureClinics[0];
        await ValidateClinicOneAsync(db, snapshot, fixtureId.HasValue);
        if (fixtureId.HasValue)
            await ValidateFixtureTenantAsync(db, fixtureId.Value);
        return new(fixtureId, snapshot);
    }

    internal static async Task<int> InsertAsync(
        AppDbContext db, string accountPassword, ClinicOneSnapshot expectedClinicOne)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Cross-tenant fixture inserts require the caller's atomic transaction.");
        PgCrossTenantFixture.ValidateAccountPassword(accountPassword);
        var before = await InspectAsync(db);
        if (before.FixtureClinicId.HasValue)
            throw new InvalidOperationException("Cross-tenant fixture already exists; duplicate creation refused.");
        if (!expectedClinicOne.IsUnchanged(before.ClinicOne))
            throw new InvalidOperationException("Clinic 1 changed after preflight.");

        var now = DateTime.UtcNow;
        var clinic = new Clinic
        {
            Name = FixtureClinicName,
            WorkHours = "SYNTHETIC ONLY: 09:00-17:00",
            CountryCode = "MN",
            ProvinceOrCapitalCode = "11",
            SoumOrDistrictCode = "1101",
            AddressLine = "SYNTHETIC CROSS-TENANT LOCATION - NOT A REAL CLINIC"
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
            "Synthetic cross-tenant director",
            "cross-tenant.director@example.invalid",
            Role.Director);
        var doctor = NewUser(
            "Synthetic cross-tenant doctor",
            "cross-tenant.doctor@example.invalid",
            Role.Doctor);
        var patientUser = NewUser(
            "Synthetic cross-tenant patient",
            "cross-tenant.patient@example.invalid",
            Role.Patient);
        db.Users.AddRange(director, doctor, patientUser);
        await db.SaveChangesAsync();

        clinic.TimeZoneId = "Asia/Ulaanbaatar";
        clinic.TimeZoneStatus = TimeZoneStatus.Verified;
        clinic.TimeZoneVerifiedAtUtc = now;
        clinic.TimeZoneVerifiedByUserId = director.Id;
        clinic.TimeZoneVerifiedClinicId = clinic.Id;

        var profile = new DoctorProfile
        {
            ClinicId = clinic.Id,
            UserId = doctor.Id,
            Specialty = "SYNTHETIC CROSS-TENANT TEST ONLY",
            PracticeStartYear = 2020,
            Biography = "Non-clinical synthetic development fixture only.",
            IsPublished = true,
            IsActive = true
        };
        var service = new Service
        {
            ClinicId = clinic.Id,
            Name = "SYNTHETIC CROSS-TENANT SERVICE",
            Description = "Synthetic placeholder only; not a real clinical offering.",
            MinPrice = 0,
            MaxPrice = 0,
            IsActive = true
        };
        var patient = new Patient
        {
            ClinicId = clinic.Id,
            UserId = patientUser.Id,
            FullName = patientUser.FullName,
            Phone = "00000002",
            IsActive = true,
            CreatedAt = now
        };
        db.DoctorProfiles.Add(profile);
        db.Services.Add(service);
        db.Patients.Add(patient);
        await db.SaveChangesAsync();

        db.DoctorServices.Add(new DoctorService
        {
            ClinicId = clinic.Id,
            DoctorProfileId = profile.Id,
            ServiceId = service.Id,
            DurationMinutes = 30,
            BufferBeforeMinutes = 0,
            BufferAfterMinutes = 0,
            IsActive = true
        });
        db.DoctorWorkingHours.AddRange(Enumerable.Range(1, 5).Select(day => new DoctorWorkingHours
        {
            ClinicId = clinic.Id,
            DoctorProfileId = profile.Id,
            DayOfWeek = day,
            StartMinute = 540,
            EndMinute = 1020
        }));
        await db.SaveChangesAsync();

        var scheduling = await new ClinicSchedulingPolicy(db, new MongoliaLocations())
            .RequireVerifiedAsync(clinic.Id);
        if (scheduling.TimeZone is null)
            throw new InvalidOperationException("Synthetic second-clinic timezone verification failed.");

        var after = await ClinicOneSnapshot.CaptureAsync(db);
        if (!expectedClinicOne.IsUnchanged(after))
            throw new InvalidOperationException("Clinic 1 changed during cross-tenant fixture insertion.");
        await ValidateFixtureTenantAsync(db, clinic.Id);
        return clinic.Id;
    }

    private static async Task ValidateClinicOneAsync(
        AppDbContext db, ClinicOneSnapshot snapshot, bool fixturePresent)
    {
        var clinic = await db.Clinics.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == BaseClinicId);
        if (clinic?.Name != BaseClinicName)
            throw new InvalidOperationException(
                "Clinic 1 identity does not match the original synthetic development fixture.");

        var expectedUsers = new HashSet<(string Email, string FullName, Role Role)>
        {
            ("development.director@example.invalid", "Synthetic development director", Role.Director)
        };
        expectedUsers.UnionWith(Enumerable.Range(1, 3).Select(index =>
            ($"development.doctor{index:00}@example.invalid",
                $"Synthetic development doctor {index:00}", Role.Doctor)));
        expectedUsers.UnionWith(Enumerable.Range(1, 5).Select(index =>
            ($"development.patient{index:00}@example.invalid",
                $"Synthetic development patient {index:00}", Role.Patient)));

        var users = await db.Users.AsNoTracking()
            .Where(user => user.ClinicId == BaseClinicId)
            .Select(user => new { user.Email, user.FullName, user.Role })
            .ToArrayAsync();
        var userIdentities = users
            .Select(user => (user.Email ?? "", user.FullName, user.Role))
            .ToHashSet();
        var expectedServices = new HashSet<string>(StringComparer.Ordinal)
        {
            "SYNTHETIC - Consultation",
            "SYNTHETIC - Cleaning",
            "SYNTHETIC - Filling",
            "SYNTHETIC - Extraction",
            "SYNTHETIC - X-ray placeholder",
            "SYNTHETIC - Follow-up"
        };
        var serviceNames = await db.Services.AsNoTracking()
            .Where(service => service.ClinicId == BaseClinicId)
            .Select(service => service.Name)
            .ToArrayAsync();

        foreach (var expected in ExpectedClinicOneCounts)
        {
            if (!snapshot.Counts.TryGetValue(expected.Key, out var actual) ||
                actual != expected.Value)
                throw new InvalidOperationException(
                    "Clinic 1 baseline counts do not match the original synthetic development fixture.");
        }

        if (snapshot.Counts.Count != ExpectedClinicOneCounts.Count + 1 ||
            users.Length != expectedUsers.Count ||
            !userIdentities.SetEquals(expectedUsers) ||
            serviceNames.Length != expectedServices.Count ||
            !new HashSet<string>(serviceNames, StringComparer.Ordinal).SetEquals(expectedServices))
            throw new InvalidOperationException(
                "Clinic 1 identities do not match the original synthetic development fixture.");

        var appointmentIds = await db.Appointments.AsNoTracking()
            .Where(appointment => appointment.ClinicId == BaseClinicId)
            .Select(appointment => appointment.Id)
            .ToArrayAsync();
        if (await db.Set<NotificationOutbox>().AsNoTracking().AnyAsync(row =>
                row.ClinicId != BaseClinicId || !appointmentIds.Contains(row.AppointmentId)))
            throw new InvalidOperationException(
                "Notification outbox contains records outside the approved Clinic 1 appointment baseline.");

        var expectedGlobalCounts = snapshot.Counts.ToDictionary(
            row => row.Key, row => row.Value, StringComparer.Ordinal);
        if (fixturePresent)
        {
            expectedGlobalCounts["Clinics"] += 1;
            expectedGlobalCounts["Users"] += 3;
            expectedGlobalCounts["DoctorProfiles"] += 1;
            expectedGlobalCounts["Patients"] += 1;
            expectedGlobalCounts["Services"] += 1;
            expectedGlobalCounts["DoctorServices"] += 1;
            expectedGlobalCounts["DoctorWorkingHours"] += 5;
        }

        var globalCounts = await ReadGlobalRecordCountsAsync(db);
        if (!globalCounts.OrderBy(row => row.Key, StringComparer.Ordinal)
                .SequenceEqual(expectedGlobalCounts.OrderBy(row => row.Key, StringComparer.Ordinal)))
            throw new InvalidOperationException(
                "Global table counts contain records outside the approved synthetic tenant scope.");
    }

    private static async Task<Dictionary<string, long>> ReadGlobalRecordCountsAsync(AppDbContext db) =>
        new(StringComparer.Ordinal)
        {
            ["Appointments"] = await db.Appointments.LongCountAsync(),
            ["ClinicPatientLinks"] = await db.Set<ClinicPatientLink>().LongCountAsync(),
            ["Clinics"] = await db.Clinics.LongCountAsync(),
            ["DoctorProfiles"] = await db.DoctorProfiles.LongCountAsync(),
            ["DoctorServices"] = await db.DoctorServices.LongCountAsync(),
            ["DoctorWorkingHours"] = await db.DoctorWorkingHours.LongCountAsync(),
            ["NotificationOutbox"] = await db.Set<NotificationOutbox>().LongCountAsync(),
            ["NotificationPreference"] = await db.Set<NotificationPreference>().LongCountAsync(),
            ["PatientAvailabilities"] = await db.PatientAvailabilities.LongCountAsync(),
            ["PatientAvailabilitySchedules"] = await db.PatientAvailabilitySchedules.LongCountAsync(),
            ["Patients"] = await db.Patients.LongCountAsync(),
            ["PhoneVerificationChallenge"] = await db.Set<PhoneVerificationChallenge>().LongCountAsync(),
            ["PlatformAccounts"] = await db.Set<PlatformAccount>().LongCountAsync(),
            ["PlatformVerificationCodes"] = await db.Set<PlatformVerificationCode>().LongCountAsync(),
            ["Services"] = await db.Services.LongCountAsync(),
            ["Users"] = await db.Users.LongCountAsync(),
            ["VerificationCodes"] = await db.VerificationCodes.LongCountAsync()
        };

    private static async Task ValidateFixtureTenantAsync(AppDbContext db, int clinicId)
    {
        if (clinicId == BaseClinicId ||
            await db.Users.CountAsync(user => user.ClinicId == clinicId) != 3 ||
            await db.Users.CountAsync(user => user.ClinicId == clinicId && user.Role == Role.Director) != 1 ||
            await db.Users.CountAsync(user => user.ClinicId == clinicId && user.Role == Role.Doctor) != 1 ||
            await db.Users.CountAsync(user => user.ClinicId == clinicId && user.Role == Role.Patient) != 1 ||
            await db.DoctorProfiles.CountAsync(row => row.ClinicId == clinicId) != 1 ||
            await db.Patients.CountAsync(row => row.ClinicId == clinicId) != 1 ||
            await db.Services.CountAsync(row => row.ClinicId == clinicId) != 1 ||
            await db.DoctorServices.CountAsync(row => row.ClinicId == clinicId) != 1 ||
            await db.DoctorWorkingHours.CountAsync(row => row.ClinicId == clinicId) != 5 ||
            await db.Appointments.CountAsync(row => row.ClinicId == clinicId) != 0)
            throw new InvalidOperationException("Cross-tenant fixture records do not match the expected shape.");
    }
}
