using DentalApi.Data;
using DentalApi.Models;
using DentalApi.PgCrossTenantFixture;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;
using BaseSeed = DentalApi.PgDevelopmentFixture.DevelopmentSeed;
using CrossTenantSeed = DentalApi.PgCrossTenantFixture.CrossTenantDevelopmentSeed;
using Fixture = DentalApi.PgCrossTenantFixture.PgCrossTenantFixture;

namespace DentalApi.IntegrationTests;

public sealed class CrossTenantDevelopmentFixtureTests
{
    [Fact]
    public void Connection_target_is_fixed_and_password_is_encoded_by_the_provider()
    {
        const string password = "local;cross-tenant'development";
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
    }

    [Fact]
    public async Task Creating_second_tenant_preserves_clinic_one_and_refuses_a_duplicate()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        await SeedClinicOneAsync(db);

        var before = await CrossTenantSeed.InspectAsync(db);
        Assert.Null(before.FixtureClinicId);
        Assert.Equal(3, before.ClinicOne.Counts["Appointments"]);

        const string accountPassword = "Synthetic-cross-tenant-password";
        int fixtureClinicId;
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            fixtureClinicId = await CrossTenantSeed.InsertAsync(db, accountPassword, before.ClinicOne);
            await transaction.CommitAsync();
        }

        Assert.NotEqual(CrossTenantSeed.BaseClinicId, fixtureClinicId);
        var after = await CrossTenantSeed.InspectAsync(db);
        Assert.Equal(fixtureClinicId, after.FixtureClinicId);
        Assert.True(before.ClinicOne.IsUnchanged(after.ClinicOne));
        Assert.Equal(2, await db.Clinics.CountAsync());
        Assert.Equal(3, await db.Appointments.CountAsync());

        var users = await db.Users.Where(user => user.ClinicId == fixtureClinicId).ToArrayAsync();
        Assert.Equal(3, users.Length);
        Assert.Equal(1, users.Count(user => user.Role == Role.Director));
        Assert.Equal(1, users.Count(user => user.Role == Role.Doctor));
        Assert.Equal(1, users.Count(user => user.Role == Role.Patient));
        Assert.All(users, user =>
        {
            Assert.EndsWith("@example.invalid", user.Email);
            Assert.True(BCrypt.Net.BCrypt.Verify(accountPassword, user.PasswordHash));
        });
        var fixtureProfile = await db.DoctorProfiles.SingleAsync(row => row.ClinicId == fixtureClinicId);
        Assert.Equal(1, await db.Patients.CountAsync(row => row.ClinicId == fixtureClinicId));
        var fixtureService = await db.Services.SingleAsync(row => row.ClinicId == fixtureClinicId);
        var assignment = await db.DoctorServices.SingleAsync(row => row.ClinicId == fixtureClinicId);
        Assert.Equal(fixtureProfile.Id, assignment.DoctorProfileId);
        Assert.Equal(fixtureService.Id, assignment.ServiceId);
        var fixtureHours = await db.DoctorWorkingHours.Where(row => row.ClinicId == fixtureClinicId)
            .OrderBy(row => row.DayOfWeek).ToArrayAsync();
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, fixtureHours.Select(row => row.DayOfWeek));
        Assert.All(fixtureHours, hours =>
        {
            Assert.Equal(fixtureProfile.Id, hours.DoctorProfileId);
            Assert.Equal(540, hours.StartMinute);
            Assert.Equal(1020, hours.EndMinute);
        });
        Assert.Equal(0, await db.Appointments.CountAsync(row => row.ClinicId == fixtureClinicId));

        await using var retry = await db.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CrossTenantSeed.InsertAsync(db, accountPassword, before.ClinicOne));
        await retry.RollbackAsync();
        var afterRetry = await CrossTenantSeed.InspectAsync(db);
        Assert.Equal(fixtureClinicId, afterRetry.FixtureClinicId);
        Assert.True(before.ClinicOne.IsUnchanged(afterRetry.ClinicOne));
    }

    [Fact]
    public async Task Inspector_rejects_an_incorrect_clinic_identity()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:; Foreign Keys=True");
        await connection.OpenAsync();
        await using var db = await CreateClinicOneDatabaseAsync(connection);
        var clinic = await db.Clinics.SingleAsync(row => row.Id == CrossTenantSeed.BaseClinicId);
        clinic.Name = "Unexpected clinic";
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => CrossTenantSeed.InspectAsync(db));
    }

    [Fact]
    public async Task Inspector_rejects_an_unexpected_user()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:; Foreign Keys=True");
        await connection.OpenAsync();
        await using var db = await CreateClinicOneDatabaseAsync(connection);
        db.Users.Add(new User
        {
            ClinicId = CrossTenantSeed.BaseClinicId,
            FullName = "Unexpected user",
            Email = "unexpected@example.invalid",
            PasswordHash = "synthetic-only",
            Role = Role.Patient
        });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => CrossTenantSeed.InspectAsync(db));
    }

    [Fact]
    public async Task Inspector_rejects_an_unexpected_clinic_record()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:; Foreign Keys=True");
        await connection.OpenAsync();
        await using var db = await CreateClinicOneDatabaseAsync(connection);
        db.Services.Add(new Service
        {
            ClinicId = CrossTenantSeed.BaseClinicId,
            Name = "Unexpected service",
            Description = "Synthetic test record",
            MinPrice = 0,
            MaxPrice = 0,
            IsActive = true
        });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => CrossTenantSeed.InspectAsync(db));
    }

    [Fact]
    public async Task Inspector_rejects_a_global_record_outside_the_approved_tenants()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:; Foreign Keys=True");
        await connection.OpenAsync();
        await using var db = await CreateClinicOneDatabaseAsync(connection);
        db.Set<PlatformAccount>().Add(new PlatformAccount
        {
            FullName = "Unexpected platform account",
            Email = "unlinked@example.invalid",
            PasswordHash = "synthetic-only"
        });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => CrossTenantSeed.InspectAsync(db));
    }

    [Fact]
    public async Task Inspector_allows_outbox_records_for_the_three_baseline_appointments()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:; Foreign Keys=True");
        await connection.OpenAsync();
        await using var db = await CreateClinicOneDatabaseAsync(connection);
        var appointment = await db.Appointments.OrderBy(row => row.Id).FirstAsync();
        db.Add(new NotificationOutbox
        {
            ClinicId = CrossTenantSeed.BaseClinicId,
            AppointmentId = appointment.Id,
            Event = NotificationEvent.Confirmed,
            Channel = NotificationChannel.Email,
            Recipient = "patient@example.invalid",
            Body = "Synthetic test notification",
            DueUtcTicks = DateTime.UtcNow.Ticks,
            NextAttemptUtcTicks = DateTime.UtcNow.Ticks,
            State = NotificationState.Pending
        });
        await db.SaveChangesAsync();

        var state = await CrossTenantSeed.InspectAsync(db);

        Assert.Equal(1, state.ClinicOne.Counts["NotificationOutbox"]);
    }

    [Fact]
    public async Task Insert_refuses_a_clinic_one_fingerprint_change_after_preflight()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:; Foreign Keys=True");
        await connection.OpenAsync();
        await using var db = await CreateClinicOneDatabaseAsync(connection);
        var before = await CrossTenantSeed.InspectAsync(db);
        var service = await db.Services.OrderBy(row => row.Id).FirstAsync();
        service.Description += " changed after preflight";
        await db.SaveChangesAsync();

        await using var transaction = await db.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CrossTenantSeed.InsertAsync(db, "Synthetic-cross-tenant-password", before.ClinicOne));
        await transaction.RollbackAsync();

        Assert.Equal(1, await db.Clinics.CountAsync());
        Assert.Null((await CrossTenantSeed.InspectAsync(db)).FixtureClinicId);
    }

    private static async Task SeedClinicOneAsync(AppDbContext db)
    {
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            await BaseSeed.InsertAsync(db, "Synthetic-clinic-one-password");
            await transaction.CommitAsync();
        }

        var patient = await db.Patients.OrderBy(row => row.Id).FirstAsync();
        var doctor = await db.DoctorProfiles.OrderBy(row => row.Id).FirstAsync();
        var service = await db.Services.OrderBy(row => row.Id).FirstAsync();
        var start = new DateTimeOffset(2027, 1, 4, 1, 0, 0, TimeSpan.Zero);
        db.Appointments.AddRange(Enumerable.Range(0, 3).Select(index =>
        {
            var appointmentStart = start.AddDays(index);
            return new Appointment
            {
                ClinicId = CrossTenantSeed.BaseClinicId,
                PatientId = patient.Id,
                DoctorProfileId = doctor.Id,
                ServiceId = service.Id,
                StartUtc = appointmentStart,
                EndUtc = appointmentStart.AddMinutes(30),
                OccupiedStartUtc = appointmentStart,
                OccupiedEndUtc = appointmentStart.AddMinutes(30),
                DurationMinutesSnapshot = 30,
                BufferBeforeMinutesSnapshot = 0,
                BufferAfterMinutesSnapshot = 0,
                ClinicTimeZoneIdSnapshot = "Asia/Ulaanbaatar",
                RequestId = Guid.NewGuid(),
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
        }));
        await db.SaveChangesAsync();
    }

    private static async Task<AppDbContext> CreateClinicOneDatabaseAsync(SqliteConnection connection)
    {
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        await SeedClinicOneAsync(db);
        return db;
    }
}
