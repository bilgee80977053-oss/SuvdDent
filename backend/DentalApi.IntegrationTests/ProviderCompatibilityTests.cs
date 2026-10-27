using System.Data;
using System.Data.Common;
using System.Text.Json;
using DentalApi.Contracts;
using DentalApi.Data;
using DentalApi.Models;
using DentalApi.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace DentalApi.IntegrationTests;

public sealed class ProviderCompatibilityTests
{
    private const string PrivateDetail = "synthetic-private-database-detail";
    private static readonly Guid Version = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static Exception Error(string state, bool wrapped) => wrapped
        ? new DbUpdateException(PrivateDetail, new PostgresException(PrivateDetail, "ERROR", "ERROR", state))
        : new PostgresException(PrivateDetail, "ERROR", "ERROR", state);

    public static IEnumerable<object[]> ConflictCases()
    {
        foreach (var operation in new[] { "read-services", "read-availability", "services", "availability",
                     "doctor-create", "doctor-update", "location", "register", "challenge", "verify" })
        foreach (var state in new[] { "23505", "40001", "40P01" })
        foreach (var wrapped in new[] { false, true })
            yield return [operation, state, wrapped];
    }

    [Theory]
    [MemberData(nameof(ConflictCases))]
    public async Task PostgreSql_transactions_select_isolation_and_return_safe_conflicts_without_connecting(
        string operation, string state, bool wrapped)
    {
        var probe = new StartFailure(Error(state, wrapped));
        await using var db = new PostgreSqlAppDbContext(new DbContextOptionsBuilder<PostgreSqlAppDbContext>()
            .UseNpgsql("Host=localhost;Database=synthetic_never_opened")
            .AddInterceptors(new SuppressConnection(), probe).Options);
        var (status, response) = await Invoke(db, operation);
        Assert.Equal(409, status);
        Assert.Equal(operation.StartsWith("read-") ? IsolationLevel.RepeatableRead : IsolationLevel.Serializable, probe.Isolation);
        Assert.DoesNotContain(PrivateDetail, JsonSerializer.Serialize(response));
        Assert.Null(db.Database.CurrentTransaction);
        Assert.Equal(ConnectionState.Closed, db.Database.GetDbConnection().State);
    }

    [Theory]
    [InlineData("23503")]
    [InlineData("23514")]
    [InlineData("08006")]
    public async Task Unrelated_PostgreSql_failures_are_not_misclassified_as_conflicts(string state)
    {
        var error = Error(state, false);
        await using var db = new PostgreSqlAppDbContext(new DbContextOptionsBuilder<PostgreSqlAppDbContext>()
            .UseNpgsql("Host=localhost;Database=synthetic_never_opened")
            .AddInterceptors(new SuppressConnection(), new StartFailure(error)).Options);
        var thrown = await Record.ExceptionAsync(() => new SchedulingService(db).ReadServices(101, 31));
        Assert.NotNull(thrown);
        Assert.Same(error, thrown.GetBaseException());
    }

    [Theory]
    [InlineData("services", "40001", false)]
    [InlineData("availability", "23505", true)]
    [InlineData("doctor-update", "40P01", false)]
    [InlineData("location", "40001", true)]
    [InlineData("verify", "40001", false)]
    public async Task Provider_conflict_at_commit_rolls_back_changes_and_preserves_other_clinics(
        string operation, string state, bool wrapped)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        var fault = new CommitFailure(Error(state, wrapped));
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).AddInterceptors(fault).Options;
        await using (var seed = new AppDbContext(options))
        {
            await seed.Database.EnsureCreatedAsync();
            seed.Clinics.AddRange(new Clinic { Id = 101, Version = Version }, new Clinic { Id = 202, Version = Version });
            await seed.SaveChangesAsync();
            seed.Users.AddRange(
                new User { Id = 11, ClinicId = 101, Role = Role.Director, IsActive = true },
                new User { Id = 12, ClinicId = 101, Role = Role.Patient, IsActive = true },
                new User { Id = 13, ClinicId = 101, Role = Role.Doctor, IsActive = true },
                new User { Id = 23, ClinicId = 202, Role = Role.Doctor, IsActive = true });
            await seed.SaveChangesAsync();
            seed.DoctorProfiles.AddRange(
                new DoctorProfile { Id = 31, ClinicId = 101, UserId = 13, Version = Version },
                new DoctorProfile { Id = 32, ClinicId = 202, UserId = 23, Version = Version });
            seed.Patients.Add(new Patient { Id = 41, ClinicId = 101, UserId = 12, IsActive = true });
            seed.PlatformAccounts.Add(new PlatformAccount { Id = 71, Email = "synthetic@example.invalid" });
            await seed.SaveChangesAsync();
            seed.PlatformVerificationCodes.Add(new PlatformVerificationCode { Id = 81, PlatformAccountId = 71,
                Channel = "email", Purpose = "registration", CodeHash = new VerificationService().HashCode("123456"),
                CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddMinutes(10) });
            await seed.SaveChangesAsync();
        }
        fault.Enabled = true;
        await using (var db = new AppDbContext(options))
        {
            var (status, response) = await Invoke(db, operation);
            Assert.Equal(409, status);
            Assert.DoesNotContain(PrivateDetail, JsonSerializer.Serialize(response));
            Assert.Null(db.Database.CurrentTransaction);
        }
        await using var verify = new AppDbContext(options);
        Assert.All(await verify.DoctorProfiles.ToArrayAsync(), p => Assert.Equal(Version, p.Version));
        Assert.All(await verify.Clinics.ToArrayAsync(), c => Assert.Equal(Version, c.Version));
        Assert.Empty(await verify.DoctorServices.ToArrayAsync());
        Assert.Empty(await verify.PatientAvailabilitySchedules.ToArrayAsync());
        Assert.Empty(await verify.PatientAvailabilities.ToArrayAsync());
        Assert.False((await verify.PlatformAccounts.SingleAsync()).IsActive);
        Assert.False((await verify.PlatformVerificationCodes.SingleAsync()).IsUsed);
        Assert.Equal(IsolationLevel.Serializable, fault.Isolation);
    }

    [Theory]
    [InlineData("services", 5)]
    [InlineData("availability", 6)]
    [InlineData("doctor-create", 5)]
    [InlineData("doctor-update", 6)]
    [InlineData("location", 5)]
    [InlineData("register", 6)]
    [InlineData("challenge", 5)]
    [InlineData("verify", 6)]
    [InlineData("doctor-create", 2067)]
    [InlineData("register", 2067)]
    [InlineData("availability", 1555)]
    public async Task SQLite_busy_locked_and_existing_unique_conflicts_keep_their_status(string operation, int code)
    {
        var error = new SqliteException(PrivateDetail, code & 255, code);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:").AddInterceptors(new StartFailure(new DbUpdateException(PrivateDetail, error))).Options);
        var (status, response) = await Invoke(db, operation);
        Assert.Equal(409, status);
        Assert.DoesNotContain(PrivateDetail, JsonSerializer.Serialize(response));
    }

    private static async Task<(int Status, object? Response)> Invoke(AppDbContext db, string operation)
    {
        var scheduling = new SchedulingService(db);
        var doctor = new DoctorProfileService(db);
        var location = new ClinicLocationService(db, new MongoliaLocations());
        var platform = new PlatformAccountService(db, new PlatformJwtService(new ConfigurationBuilder().Build()),
            new VerificationService(), new NoDelivery());
        if (operation.StartsWith("doctor-"))
        {
            var result = operation == "doctor-create"
                ? await doctor.CreateAsync(101, new(13, new("Dentistry", 2015, null, null)))
                : await doctor.UpdateAsync(101, 31, null, Version, new("Changed", 2015, null, null));
            return (result.Status, result);
        }
        var response = operation switch
        {
            "read-services" => await scheduling.ReadServices(101, 31),
            "read-availability" => await scheduling.ReadAvailability(101, 12),
            "services" => await scheduling.WriteServices(101, 31, new(Version, [])),
            "availability" => await scheduling.WriteAvailability(101, 12, new(Guid.Empty, [])),
            "location" => await location.Update(101, 11, new(Version, new("MN", "11", "1101"), "Synthetic address")),
            "register" => await platform.Register(new("Synthetic", "synthetic@example.invalid", null, "Synthetic-password-123")),
            "challenge" => await platform.Challenge("synthetic@example.invalid", false),
            "verify" => await platform.Verify("synthetic@example.invalid", "123456"),
            _ => throw new InvalidOperationException(operation)
        };
        return (response.Status, response);
    }

    private sealed class NoDelivery : IPlatformMessageSender
    {
        public bool IsAvailable => true;
        public Task SendAsync(string channel, string destination, string purpose, string code) =>
            throw new InvalidOperationException("Delivery must never happen after a database conflict.");
    }

    // Suppress opening before the transaction interceptor aborts. No socket is opened.
    private sealed class SuppressConnection : DbConnectionInterceptor
    {
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection,
            ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(InterceptionResult.Suppress());
    }
    private sealed class StartFailure(Exception error) : DbTransactionInterceptor
    {
        public IsolationLevel? Isolation { get; private set; }
        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection,
            TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        {
            Isolation = eventData.IsolationLevel;
            throw error;
        }
    }
    private sealed class CommitFailure(Exception error) : DbTransactionInterceptor
    {
        public bool Enabled { get; set; }
        public IsolationLevel? Isolation { get; private set; }
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (Enabled) { Isolation = transaction.IsolationLevel; throw error; }
            return ValueTask.FromResult(result);
        }
    }
}
