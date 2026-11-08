using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DentalApi.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DentalApi.Demo;

public sealed record DemoSlot(string Label, int DoctorProfileId, int ServiceId, DateTimeOffset StartUtc);
public sealed record DemoManifest(string DatasetVersion, string DirectoryName, string DatabaseFile,
    string SchemaSha256, DateTime PreparedAtUtc, int ClinicId, int PlatformAccountId,
    int[] DoctorProfileIds, int[] ServiceIds, int SampleAppointmentId, DateTimeOffset SampleStartUtc,
    DemoSlot[] CandidateSlots);

public static class DemoDatabaseSafety
{
    public const string DatasetVersion = "nexora-demo-v1";
    public const string DatabaseFile = "nexora-demo.db";
    public const string ManifestFile = "demo-manifest.json";

    public static string ValidateRoot(string? input)
    {
        var root = AbsolutePath(input);
        if (!Path.GetFileName(root).Equals("NexoraDental-Demos", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("DEMO root must be named NexoraDental-Demos.");
        RejectLinks(root);
        return root;
    }

    private static string AbsolutePath(string? input)
    {
        if (string.IsNullOrWhiteSpace(input) || !Path.IsPathFullyQualified(input) || input.StartsWith(@"\\", StringComparison.Ordinal) ||
            input.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(x => x is "." or ".."))
            throw new InvalidOperationException("An explicit local absolute DEMO path is required.");
        return Path.GetFullPath(input).TrimEnd(Path.DirectorySeparatorChar);
    }

    public static void RejectLinks(string path)
    {
        for (string? part = path; part is not null; part = Path.GetDirectoryName(part))
            if ((Directory.Exists(part) || File.Exists(part)) && (File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Linked DEMO paths are not permitted.");
    }

    public static string ValidateDatabasePath(string? rootInput, string? databaseInput, bool requirePrepared)
    {
        var root = ValidateRoot(rootInput);
        var path = AbsolutePath(databaseInput);
        var directory = Path.GetDirectoryName(path)!;
        if (!Path.GetFileName(path).Equals(DatabaseFile, StringComparison.Ordinal) ||
            !string.Equals(Path.GetDirectoryName(directory), root, StringComparison.OrdinalIgnoreCase) ||
            !Regex.IsMatch(Path.GetFileName(directory), @"\Ademo-[0-9]{8}-[0-9]{6}-[a-f0-9]{32}\z"))
            throw new InvalidOperationException("Unexpected DEMO database location.");
        RejectLinks(path);
        RejectLinks(Path.Combine(directory, ManifestFile));
        if (requirePrepared && (!File.Exists(path) || new FileInfo(path).Length == 0 ||
            !File.Exists(Path.Combine(directory, ManifestFile))))
            throw new InvalidOperationException("DEMO preparation is incomplete.");
        return path;
    }

    public static string ConnectionString(string path) => new SqliteConnectionStringBuilder
    {
        DataSource = path, Mode = SqliteOpenMode.ReadWrite, ForeignKeys = true, Pooling = false
    }.ToString();

    public static string StartupConnectionString(IConfiguration config)
    {
        var path = ValidateDatabasePath(config["Demo:Root"], config["Demo:DatabasePath"], true);
        return ConnectionString(path); // Never uses ConnectionStrings:Default or ReadWriteCreate.
    }

    public static void ValidateRuntimeConfiguration(IConfiguration config)
    {
        _ = StartupConnectionString(config);
        if (Encoding.UTF8.GetByteCount(config["Jwt:Key"] ?? "") < 32 ||
            string.IsNullOrWhiteSpace(config["Jwt:Issuer"]) || string.IsNullOrWhiteSpace(config["Jwt:Audience"]))
            throw new InvalidOperationException("Private DEMO JWT configuration is required.");
        _ = new Services.PlatformJwtService(config).Settings();
        if (config.GetSection("Email").AsEnumerable().Any(x => !string.IsNullOrEmpty(x.Value)))
            throw new InvalidOperationException("SMTP configuration is not permitted in DEMO.");
    }

    public static async Task<string> SchemaHashAsync(AppDbContext db)
    {
        await db.Database.OpenConnectionAsync();
        using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT type,name,tbl_name,sql FROM sqlite_master WHERE name NOT LIKE 'sqlite_%' AND name <> '__EFMigrationsLock' ORDER BY type,name";
        using var reader = await command.ExecuteReaderAsync();
        var objects = new List<string?[]>();
        while (await reader.ReadAsync())
            objects.Add(Enumerable.Range(0, 4).Select(i => reader.IsDBNull(i) ? null : reader.GetString(i)).ToArray());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(objects))));
    }

    public static async Task<DemoManifest> ValidateReadyAsync(AppDbContext db, string root, string databasePath)
    {
        var path = ValidateDatabasePath(root, databasePath, true);
        var actual = new SqliteConnectionStringBuilder(db.Database.GetConnectionString());
        if (actual.Mode != SqliteOpenMode.ReadWrite || actual.ForeignKeys != true ||
            !string.Equals(actual.DataSource, path, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The DEMO context must own the explicitly selected database.");
        var manifestPath = Path.Combine(Path.GetDirectoryName(path)!, ManifestFile);
        if (new FileInfo(manifestPath).Length > 128 * 1024) throw new InvalidOperationException("Invalid DEMO manifest.");
        var manifest = JsonSerializer.Deserialize<DemoManifest>(await File.ReadAllTextAsync(manifestPath));
        if (manifest is null || manifest.DatasetVersion != DatasetVersion || manifest.DatabaseFile != DatabaseFile ||
            manifest.DirectoryName != Path.GetFileName(Path.GetDirectoryName(path)) ||
            !Regex.IsMatch(manifest.SchemaSha256 ?? "", @"\A[A-F0-9]{64}\z"))
            throw new InvalidOperationException("Invalid DEMO manifest.");
        var expected = db.Database.GetMigrations().ToArray();
        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        if (db.Database.HasPendingModelChanges() || !expected.SequenceEqual(applied) || await SchemaHashAsync(db) != manifest.SchemaSha256)
            throw new InvalidOperationException("DEMO schema is incomplete or incompatible; startup never migrates it.");
        using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "PRAGMA foreign_key_check";
        using (var reader = await command.ExecuteReaderAsync())
            if (await reader.ReadAsync()) throw new InvalidOperationException("DEMO foreign key validation failed.");
        command.CommandText = "PRAGMA integrity_check";
        if ((string?)await command.ExecuteScalarAsync() != "ok") throw new InvalidOperationException("DEMO integrity validation failed.");
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='trigger' AND name IN ('TR_Appointments_NoOverlap_INSERT','TR_Appointments_NoOverlap_UPDATE')";
        if (Convert.ToInt64(await command.ExecuteScalarAsync()) != 2) throw new InvalidOperationException("DEMO overlap protection is missing.");
        if (!await db.Clinics.AnyAsync(c => c.Id == manifest.ClinicId && c.TimeZoneId == "Asia/Ulaanbaatar" && c.TimeZoneStatus == Models.TimeZoneStatus.Verified) ||
            !await db.PlatformAccounts.AnyAsync(a => a.Id == manifest.PlatformAccountId && a.IsActive && a.IsEmailVerified) ||
            !await db.Appointments.AnyAsync(a => a.Id == manifest.SampleAppointmentId && a.ClinicId == manifest.ClinicId && a.PlatformAccountId == manifest.PlatformAccountId))
            throw new InvalidOperationException("DEMO dataset is incomplete.");
        return manifest;
    }
}
