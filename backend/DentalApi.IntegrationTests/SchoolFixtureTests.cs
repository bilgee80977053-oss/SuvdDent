using System.Security.Cryptography;
using DentalApi.Contracts;
using DentalApi.Data;
using DentalApi.SchoolFixture;
using DentalApi.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DentalApi.IntegrationTests;

public sealed class SchoolFixtureTests
{
    [Fact]
    public void Missing_opt_in_and_private_password_fail_closed()
    {
        Assert.Throws<InvalidOperationException>(() => FixtureSafety.RequireOptIn(null));
        Assert.Throws<InvalidOperationException>(() => FixtureSafety.RequireOptIn("true"));
        FixtureSafety.RequireOptIn(FixtureSafety.OptInValue);
        Assert.Throws<InvalidOperationException>(() => FixtureSafety.RequirePassword(null));
        Assert.Throws<InvalidOperationException>(() => FixtureSafety.RequirePassword(new string('x', 7)));
        Assert.Throws<InvalidOperationException>(() => FixtureSafety.RequirePassword(new string('x', 73)));
    }

    [Fact]
    public void Target_must_be_a_direct_named_child_of_school_test_root()
    {
        var root = Path.Combine(Path.GetTempPath(), "NexoraDental", "school-test");
        var good = Path.Combine(root, "step5-test.sqlite");
        Assert.Equal(good, FixtureSafety.Resolve(good, root));
        foreach (var path in new[] { "step5-relative.sqlite", Path.Combine(root, "local-dev.db"), Path.Combine(root, "nested", "step5-test.sqlite"), Path.Combine(root + "-other", "step5-test.sqlite"), Path.Combine(root, "..", "step5-test.sqlite") })
            Assert.Throws<InvalidOperationException>(() => FixtureSafety.Resolve(path, root));
    }

    [Fact]
    public void Existing_file_or_each_sidecar_is_preserved_and_rejected()
    {
        var root = Path.Combine(Path.GetTempPath(), "NexoraSchoolSafety-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var target = Path.Combine(root, "step5-sentinel.sqlite");
        try
        {
            foreach (var suffix in new[] { "", "-wal", "-shm", "-journal", ".fixture.json" })
            {
                var sentinel = target + suffix;
                // Plain-text owned sentinel, never a SQLite database or user file.
                File.WriteAllText(sentinel, "OWNED TEST SENTINEL - NOT A DATABASE");
                try
                {
                    Assert.Throws<InvalidOperationException>(() => FixtureSafety.RequireNew(target));
                    Assert.Equal("OWNED TEST SENTINEL - NOT A DATABASE", File.ReadAllText(sentinel));
                }
                finally { File.Delete(sentinel); }
            }
            FixtureSafety.RequireNew(target);
        }
        finally { Directory.Delete(root); }
    }

    [Fact]
    public async Task Synthetic_fixture_satisfies_real_booking_rules_in_memory_only()
    {
        // This test is NOT a persisted-file or Flutter/network rehearsal.
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.MigrateAsync();
        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        await SchoolData.SeedAsync(db, Guid.NewGuid(), password);
        Assert.Equal(11, (await db.Database.GetAppliedMigrationsAsync()).Count());
        Assert.Equal(3, await db.Users.CountAsync());
        var user = await db.Users.SingleAsync(u => u.Email == SchoolData.PatientEmail);
        Assert.True(user.IsActive && user.IsEmailVerified);
        Assert.True(BCrypt.Net.BCrypt.Verify(password, user.PasswordHash));
        var clinic = await db.Clinics.SingleAsync();
        var doctor = await db.DoctorProfiles.SingleAsync();
        var service = await db.Services.SingleAsync();
        Assert.NotEqual(101, clinic.Id); Assert.NotEqual(202, clinic.Id);
        var policy = new ClinicSchedulingPolicy(db, new MongoliaLocations());
        var appointments = new AppointmentService(db, policy, TimeProvider.System);
        var tomorrow = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Ulaanbaatar"))).AddDays(1);
        var slots = await appointments.Slots(clinic.Id, user.Id, doctor.Id, service.Id, tomorrow.ToString("yyyy-MM-dd"));
        Assert.Equal(200, slots.Status);
        var page = Assert.IsType<AppointmentSlotsResponse>(slots.Data);
        Assert.NotEmpty(page.Slots);
        var request = new CreateAppointmentRequest(doctor.Id, service.Id, page.Slots[0].StartUtc.ToString("O"), Guid.NewGuid());
        Assert.Equal(201, (await appointments.Create(clinic.Id, user.Id, request)).Status);
        Assert.Equal(200, (await appointments.Create(clinic.Id, user.Id, request)).Status);
        Assert.Equal(409, (await appointments.Create(clinic.Id, user.Id, request with { RequestId = Guid.NewGuid() })).Status);
        Assert.Equal(1, await db.Appointments.CountAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => SchoolData.SeedAsync(db, Guid.NewGuid(), password));
        Assert.Equal(1, await db.Appointments.CountAsync());
    }
    [Fact]
    public void Startup_configuration_rejects_public_bindings_extra_origins_and_wrong_database_without_connecting()
    {
        var path = Path.Combine(FixtureSafety.Root, "step5-config-test.sqlite");
        var values = new Dictionary<string, string?> {
            ["Database:Provider"] = "Sqlite", ["ConnectionStrings:Default"] = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWrite, Pooling = false, ForeignKeys = true }.ToString(),
            ["Jwt:Key"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)), ["Jwt:Issuer"] = "SchoolClinic", ["Jwt:Audience"] = "SchoolFlutter",
            ["PlatformJwt:Key"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)), ["PlatformJwt:Issuer"] = "SchoolPlatform", ["PlatformJwt:Audience"] = "SchoolPlatformClient",
            ["AllowedHosts"] = "127.0.0.1;localhost", ["Cors:AllowedOrigins:0"] = "http://localhost:7357", ["urls"] = "http://127.0.0.1:5018",
            ["Email:Host"] = "127.0.0.1", ["Email:Port"] = "9", ["Email:From"] = "school-sender@example.invalid", ["Email:Username"] = "school-sender@example.invalid", ["Email:Password"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        };
        IConfiguration Config(Dictionary<string, string?> data) => new ConfigurationBuilder().AddInMemoryCollection(data).Build();
        FixtureSafety.RequireSafeStartup(Config(values), path);
        foreach (var bad in new[] {
            new KeyValuePair<string, string?>("Database:Provider", "PostgreSQL"),
            new("ConnectionStrings:Default", new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false, ForeignKeys = true }.ToString()),
            new("ConnectionStrings:Default", new SqliteConnectionStringBuilder { DataSource = Path.Combine(FixtureSafety.Root, "step5-other.sqlite"), Mode = SqliteOpenMode.ReadWrite, Pooling = false, ForeignKeys = true }.ToString()),
            new("urls", "http://0.0.0.0:5018"), new("Cors:AllowedOrigins:1", "http://localhost:9999"), new("ForwardedHeaders:Enabled", "true"),
            new("Kestrel:Endpoints:Public:Url", "http://0.0.0.0:5018"), new("HTTPS_PORT", "7235"), new("AllowedHosts", "*") })
        {
            var changed = new Dictionary<string, string?>(values) { [bad.Key] = bad.Value };
            Assert.Throws<InvalidOperationException>(() => FixtureSafety.RequireSafeStartup(Config(changed), path));
        }
        Assert.False(File.Exists(path));
    }}
