using DentalApi.Data;
using DentalApi.Models;
using DentalApi.PgDevelopmentFixture;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;
using Fixture = DentalApi.PgDevelopmentFixture.PgDevelopmentFixture;

namespace DentalApi.IntegrationTests;

public sealed class PgDevelopmentFixtureTests
{
    [Fact]
    public void Connection_string_is_fixed_and_safely_encodes_private_password()
    {
        const string password = "local;development'password";
        var settings = new NpgsqlConnectionStringBuilder(Fixture.BuildConnectionString(password));

        Assert.Equal("127.0.0.1", settings.Host);
        Assert.Equal(5432, settings.Port);
        Assert.Equal("nexora_dental_dev", settings.Database);
        Assert.Equal("nexora_dev", settings.Username);
        Assert.Equal(password, settings.Password);
        Assert.Equal("public", settings.SearchPath);
        Assert.False(settings.Pooling);
        Assert.False(settings.Enlist);
        Assert.False(settings.LogParameters);
        Assert.False(settings.IncludeErrorDetail);
        Assert.Equal(17, Fixture.TableCounts.Length);
    }

    [Fact]
    public void Target_identity_query_normalizes_inet_addresses_to_host_addresses()
    {
        Assert.Contains("COALESCE(host(inet_server_addr()), '')", Fixture.TargetIdentitySql);
    }

    [Theory]
    [InlineData("nexora_dental_dev|nexora_dev|127.0.0.1|5432|public", true)]
    [InlineData("nexora_dental_dev|nexora_dev|127.0.0.1/32|5432|public", false)]
    [InlineData("nexora_dental_test|nexora_dev|127.0.0.1|5432|public", false)]
    [InlineData("nexora_dental_dev|postgres|127.0.0.1|5432|public", false)]
    [InlineData("nexora_dental_dev|nexora_dev|localhost|5432|public", false)]
    [InlineData("nexora_dental_dev|nexora_dev|127.0.0.1|5433|public", false)]
    [InlineData("nexora_dental_dev|nexora_dev|127.0.0.1|5432|other_schema", false)]
    public void Target_identity_requires_every_fixed_connection_attribute(string identity, bool expected) =>
        Assert.Equal(expected, Fixture.HasApprovedTargetIdentity(identity));

    [Theory]
    [InlineData(17, false)]
    [InlineData(18, true)]
    [InlineData(19, false)]
    public void PostgreSql_version_must_match_the_approved_major_version(int majorVersion, bool expected) =>
        Assert.Equal(expected, Fixture.HasApprovedPostgreSqlVersion(majorVersion));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Connection_requires_a_private_database_password(string? password) =>
        Assert.Throws<InvalidOperationException>(
            () => Fixture.BuildConnectionString(password!));

    [Theory]
    [InlineData("short")]
    [InlineData("this password is longer than seventy-two utf8 bytes when measured because it is a deliberately excessive fixture password string")]
    public void Synthetic_account_password_is_bounded(string password) =>
        Assert.Throws<InvalidOperationException>(
            () => Fixture.ValidateAccountPassword(password));

    [Theory]
    [InlineData("a sufficiently long private fixture password")]
    [InlineData("энэ бол нууц үг 1234")]
    public void Synthetic_account_password_accepts_valid_utf8(string password) =>
        Fixture.ValidateAccountPassword(password);

    [Fact]
    public async Task Seed_is_tenant_scoped_hashed_and_rejected_once_application_data_exists()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var accountPassword = "Synthetic-development-only-password";
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            var clinicId = await DevelopmentSeed.InsertAsync(db, accountPassword);
            await transaction.CommitAsync();
            Assert.True(clinicId > 0);
        }

        Assert.Equal(1, await db.Clinics.CountAsync());
        Assert.Equal(9, await db.Users.CountAsync());
        Assert.Equal(1, await db.Users.CountAsync(user => user.Role == Role.Director));
        Assert.Equal(3, await db.Users.CountAsync(user => user.Role == Role.Doctor));
        Assert.Equal(5, await db.Users.CountAsync(user => user.Role == Role.Patient));
        Assert.Equal(3, await db.DoctorProfiles.CountAsync());
        Assert.Equal(5, await db.Patients.CountAsync());
        Assert.Equal(6, await db.Services.CountAsync());
        Assert.Equal(6, await db.DoctorServices.CountAsync());
        Assert.Equal(15, await db.DoctorWorkingHours.CountAsync());
        Assert.All(await db.Users.ToListAsync(), user =>
        {
            Assert.True(BCrypt.Net.BCrypt.Verify(accountPassword, user.PasswordHash));
            Assert.EndsWith("@example.invalid", user.Email);
            Assert.Equal(1, user.ClinicId);
        });
        Assert.All(await db.Patients.ToListAsync(), patient =>
        {
            Assert.Equal(1, patient.ClinicId);
            Assert.NotNull(patient.UserId);
        });
        Assert.All(await db.DoctorServices.ToListAsync(), assignment =>
            Assert.Equal(1, assignment.ClinicId));
        Assert.All(await db.DoctorWorkingHours.ToListAsync(), hours =>
        {
            Assert.Equal(1, hours.ClinicId);
            Assert.InRange(hours.DayOfWeek, 1, 5);
            Assert.Equal(540, hours.StartMinute);
            Assert.Equal(1020, hours.EndMinute);
        });
        Assert.Equal(0, await db.VerificationCodes.CountAsync());
        Assert.Equal(0, await db.PlatformVerificationCodes.CountAsync());
        Assert.Equal(0, await db.Set<PhoneVerificationChallenge>().CountAsync());

        await using var retryTransaction = await db.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DevelopmentSeed.InsertAsync(db, accountPassword));
    }
}
