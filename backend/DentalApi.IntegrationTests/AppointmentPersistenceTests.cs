using System.Text.Json;
using DentalApi.Data;
using DentalApi.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace DentalApi.IntegrationTests;

public sealed class AppointmentPersistenceTests
{
    private const string Previous = "20261026131551_AddPlatformAccounts";

    private sealed class Database : IAsyncDisposable
    {
        internal SqliteConnection Connection { get; } = new("Data Source=:memory:;Foreign Keys=True");
        internal AppDbContext Db { get; }
        internal string Target => Db.Database.GetMigrations().Single(m => m.EndsWith("_AddAppointmentFoundation"));
        private Database() => Db = new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(Connection).Options);

        internal static async Task<Database> Create(bool upgrade = true)
        {
            var test = new Database();
            try
            {
                await test.Connection.OpenAsync();
                await test.Db.GetService<IMigrator>().MigrateAsync(Previous);
                test.Db.Clinics.AddRange(new Clinic { Id = 101, Name = "Synthetic A" }, new Clinic { Id = 202, Name = "Synthetic B" });
                await test.Db.SaveChangesAsync();
                test.Db.Users.AddRange(
                    new User { Id = 11, ClinicId = 101, Role = Role.Patient, FullName = "Synthetic A", PasswordHash = "synthetic-only" },
                    new User { Id = 12, ClinicId = 202, Role = Role.Patient, FullName = "Synthetic B", PasswordHash = "synthetic-only" },
                    new User { Id = 13, ClinicId = 101, Role = Role.Doctor, FullName = "Synthetic doctor A", PasswordHash = "synthetic-only" },
                    new User { Id = 23, ClinicId = 202, Role = Role.Doctor, FullName = "Synthetic doctor B", PasswordHash = "synthetic-only" });
                await test.Db.SaveChangesAsync();
                test.Db.Patients.AddRange(new Patient { Id = 41, ClinicId = 101, UserId = 11, FullName = "Synthetic A" }, new Patient { Id = 42, ClinicId = 202, UserId = 12, FullName = "Synthetic B" });
                test.Db.DoctorProfiles.AddRange(new DoctorProfile { Id = 31, ClinicId = 101, UserId = 13 }, new DoctorProfile { Id = 32, ClinicId = 202, UserId = 23 });
                test.Db.Services.AddRange(new Service { Id = 51, ClinicId = 101, Name = "Synthetic A" }, new Service { Id = 61, ClinicId = 202, Name = "Synthetic B" });
                test.Db.PlatformAccounts.Add(new PlatformAccount { Id = 91, Email = "synthetic@example.invalid", PasswordHash = "synthetic-only" });
                await test.Db.SaveChangesAsync();
                test.Db.DoctorServices.Add(new DoctorService { Id = 71, ClinicId = 101, DoctorProfileId = 31, ServiceId = 51,
                    DurationMinutes = 30, BufferBeforeMinutes = 5, BufferAfterMinutes = 10 });
                test.Db.DoctorWorkingHours.Add(new DoctorWorkingHours { Id = 81, ClinicId = 101, DoctorProfileId = 31, DayOfWeek = 1, StartMinute = 540, EndMinute = 1020 });
                test.Db.VerificationCodes.Add(new VerificationCode { Id = 101, UserId = 11, Channel = "email", Purpose = "registration", CodeHash = "synthetic-only", ExpiresAt = DateTime.UtcNow.AddMinutes(10) });
                test.Db.PlatformVerificationCodes.Add(new PlatformVerificationCode { Id = 111, PlatformAccountId = 91, Channel = "email", Purpose = "registration", CodeHash = "synthetic-only", ExpiresAt = DateTime.UtcNow.AddMinutes(10) });
                test.Db.ClinicPatientLinks.Add(new ClinicPatientLink { Id = 121, PlatformAccountId = 91, ClinicId = 202, PatientId = 42 });
                test.Db.PatientAvailabilitySchedules.Add(new PatientAvailabilitySchedule { ClinicId = 202, PatientId = 42 });
                await test.Db.SaveChangesAsync();
                test.Db.PatientAvailabilities.Add(new PatientAvailability { ClinicId = 202, PatientId = 42,
                    StartUtc = new(2026, 11, 2, 1, 0, 0, TimeSpan.Zero), EndUtc = new(2026, 11, 2, 2, 0, 0, TimeSpan.Zero) });
                await test.Db.SaveChangesAsync();
                test.Db.ChangeTracker.Clear();
                if (upgrade) await test.Db.GetService<IMigrator>().MigrateAsync(test.Target);
                return test;
            }
            catch { await test.DisposeAsync(); throw; }
        }

        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await Connection.DisposeAsync(); }
    }

    private static Appointment Valid(Guid? request = null, DateTimeOffset? start = null) => new()
    {
        ClinicId = 101, PatientId = 41, DoctorProfileId = 31, ServiceId = 51,
        StartUtc = start ?? new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.FromHours(8)).AddTicks(1234567),
        EndUtc = (start ?? new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.FromHours(8)).AddTicks(1234567)).AddMinutes(30),
        OccupiedStartUtc = (start ?? new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.FromHours(8)).AddTicks(1234567)).AddMinutes(-5),
        OccupiedEndUtc = (start ?? new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.FromHours(8)).AddTicks(1234567)).AddMinutes(40),
        DurationMinutesSnapshot = 30, BufferBeforeMinutesSnapshot = 5, BufferAfterMinutesSnapshot = 10,
        ClinicTimeZoneIdSnapshot = "Asia/Ulaanbaatar", RequestId = request ?? Guid.NewGuid()
    };

    private static async Task<int> Insert(Database test, Appointment? appointment = null)
    {
        appointment ??= Valid();
        test.Db.Appointments.Add(appointment);
        await test.Db.SaveChangesAsync();
        return appointment.Id;
    }

    private static async Task<string> Query(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand(); command.CommandText = sql;
        using var reader = await command.ExecuteReaderAsync(); var rows = new List<object?[]>();
        while (await reader.ReadAsync())
        {
            var row = new object?[reader.FieldCount];
            for (var i = 0; i < row.Length; i++) row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }
        return JsonSerializer.Serialize(rows);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Additive_migration_and_generated_SQL_preserve_every_existing_table_row_and_schema(bool script)
    {
        await using var test = await Database.Create(upgrade: false);
        var tables = new List<string>();
        using (var cmd = test.Connection.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name NOT LIKE '__EF%' ORDER BY name";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
        }
        Assert.Equal(13, tables.Count);
        var before = new Dictionary<string, string>();
        foreach (var table in tables) before[table] = await Query(test.Connection, $"SELECT * FROM \"{table}\" ORDER BY rowid");
        const string schemaQuery = "SELECT type,name,tbl_name,sql FROM sqlite_master WHERE name NOT LIKE 'sqlite_%' AND name NOT LIKE '__EF%' AND tbl_name <> 'Appointments' ORDER BY type,name";
        var schema = await Query(test.Connection, schemaQuery);
        var migrator = test.Db.GetService<IMigrator>();
        if (script)
            await test.Db.Database.ExecuteSqlRawAsync(migrator.GenerateScript(Previous, test.Target));
        else
            await migrator.MigrateAsync(test.Target);
        foreach (var table in tables) Assert.Equal(before[table], await Query(test.Connection, $"SELECT * FROM \"{table}\" ORDER BY rowid"));
        Assert.Equal(schema, await Query(test.Connection, schemaQuery));
        Assert.Empty(await test.Db.Appointments.ToArrayAsync());
        Assert.Empty(await test.Db.Database.GetPendingMigrationsAsync());
        Assert.False(test.Db.Database.HasPendingModelChanges());
        Assert.Equal("[]", await Query(test.Connection, "PRAGMA foreign_key_check"));
        Assert.Equal("[[\"ok\"]]", await Query(test.Connection, "PRAGMA integrity_check"));
        await Insert(test);
        Assert.True((await test.Db.Appointments.SingleAsync()).Id > 0);
    }

    [Fact]
    public async Task Valid_values_round_trip_as_UTC_and_snapshot_history_survives_configuration_changes()
    {
        await using var test = await Database.Create();
        var original = Valid();
        var id = await Insert(test, original);
        await test.Db.DoctorServices.Where(s => s.Id == 71).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.DurationMinutes, 45).SetProperty(x => x.BufferBeforeMinutes, 0).SetProperty(x => x.BufferAfterMinutes, 0));
        await test.Db.Clinics.Where(c => c.Id == 101).ExecuteUpdateAsync(s => s.SetProperty(c => c.TimeZoneId, "Asia/Hovd"));
        var row = await test.Db.Appointments.AsNoTracking().SingleAsync(a => a.Id == id);
        Assert.Equal(original.StartUtc.UtcTicks, row.StartUtc.UtcTicks);
        Assert.Equal(TimeSpan.Zero, row.StartUtc.Offset);
        Assert.Equal(TimeSpan.Zero, row.EndUtc.Offset);
        Assert.Equal(TimeSpan.Zero, row.OccupiedStartUtc.Offset);
        Assert.Equal(TimeSpan.Zero, row.OccupiedEndUtc.Offset);
        Assert.Equal(TimeSpan.Zero, row.CreatedAtUtc.Offset);
        Assert.Equal(original.CreatedAtUtc.UtcTicks, row.CreatedAtUtc.UtcTicks);
        Assert.Equal(30, row.DurationMinutesSnapshot);
        Assert.Equal(5, row.BufferBeforeMinutesSnapshot);
        Assert.Equal(10, row.BufferAfterMinutesSnapshot);
        Assert.Equal("Asia/Ulaanbaatar", row.ClinicTimeZoneIdSnapshot);
        Assert.Equal(AppointmentStatus.Confirmed, row.Status);
        Assert.Equal(original.RequestId, row.RequestId);
        Assert.Equal("[[\"integer\",\"Confirmed\"]]", await Query(test.Connection, "SELECT typeof(StartUtc),Status FROM Appointments"));
    }

    [Theory]
    [InlineData("ClinicId", 202)]
    [InlineData("PatientId", 42)]
    [InlineData("DoctorProfileId", 32)]
    [InlineData("ServiceId", 61)]
    [InlineData("PatientId", 9999)]
    [InlineData("DoctorProfileId", 9999)]
    [InlineData("ServiceId", 9999)]
    public async Task Database_rejects_cross_tenant_and_missing_references(string column, int value)
    {
        await using var test = await Database.Create();
        var id = await Insert(test);
        Assert.Contains(column, new[] { "ClinicId", "PatientId", "DoctorProfileId", "ServiceId" });
        using var command = test.Connection.CreateCommand();
        command.CommandText = $"UPDATE Appointments SET \"{column}\"=@value WHERE Id=@id";
        command.Parameters.AddWithValue("value", value); command.Parameters.AddWithValue("id", id);
        var error = await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(787, error.SqliteExtendedErrorCode);
        var row = await test.Db.Appointments.AsNoTracking().SingleAsync();
        Assert.Equal(101, row.ClinicId); Assert.Equal(41, row.PatientId);
        Assert.Equal(31, row.DoctorProfileId); Assert.Equal(51, row.ServiceId);
        Assert.Equal("[]", await Query(test.Connection, "PRAGMA foreign_key_check"));
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("reversed")]
    [InlineData("occupied-start")]
    [InlineData("occupied-end")]
    [InlineData("duration-zero")]
    [InlineData("duration-too-large")]
    [InlineData("duration-mismatch")]
    [InlineData("negative-before")]
    [InlineData("negative-after")]
    [InlineData("before-mismatch")]
    [InlineData("after-mismatch")]
    [InlineData("total-too-large")]
    [InlineData("unknown-status")]
    [InlineData("empty-request")]
    [InlineData("empty-timezone")]
    [InlineData("long-timezone")]
    public async Task Database_rejects_invalid_half_open_intervals_snapshot_values_and_required_metadata(string scenario)
    {
        await using var test = await Database.Create();
        var appointment = Valid();
        switch (scenario)
        {
            case "empty": appointment.EndUtc = appointment.StartUtc; break;
            case "reversed": appointment.EndUtc = appointment.StartUtc.AddMinutes(-1); break;
            case "occupied-start": appointment.OccupiedStartUtc = appointment.StartUtc.AddMinutes(1); break;
            case "occupied-end": appointment.OccupiedEndUtc = appointment.EndUtc.AddMinutes(-1); break;
            case "duration-zero": appointment.DurationMinutesSnapshot = 0; break;
            case "duration-too-large": appointment.DurationMinutesSnapshot = 1441; break;
            case "duration-mismatch": appointment.DurationMinutesSnapshot = 31; break;
            case "negative-before": appointment.BufferBeforeMinutesSnapshot = -1; break;
            case "negative-after": appointment.BufferAfterMinutesSnapshot = -1; break;
            case "before-mismatch": appointment.BufferBeforeMinutesSnapshot = 6; break;
            case "after-mismatch": appointment.BufferAfterMinutesSnapshot = 11; break;
            case "total-too-large": appointment.BufferAfterMinutesSnapshot = 1439; break;
            case "unknown-status": appointment.Status = (AppointmentStatus)999; break;
            case "empty-request": appointment.RequestId = Guid.Empty; break;
            case "empty-timezone": appointment.ClinicTimeZoneIdSnapshot = " "; break;
            case "long-timezone": appointment.ClinicTimeZoneIdSnapshot = new string('x', 101); break;
            default: throw new InvalidOperationException(scenario);
        }
        test.Db.Appointments.Add(appointment);
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => test.Db.SaveChangesAsync());
        Assert.Equal(275, Assert.IsType<SqliteException>(error.InnerException).SqliteExtendedErrorCode);
        Assert.Empty(await test.Db.Appointments.AsNoTracking().ToArrayAsync());
    }

    [Fact]
    public async Task Raw_SQL_cannot_store_out_of_range_ticks()
    {
        await using var test = await Database.Create();
        var id = await Insert(test);
        foreach (var column in new[] { "OccupiedStartUtc", "CreatedAtUtc" })
        {
            using var command = test.Connection.CreateCommand();
            command.CommandText = $"UPDATE Appointments SET \"{column}\"=-1 WHERE Id=@id";
            command.Parameters.AddWithValue("id", id);
            var error = await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync());
            Assert.Equal(275, error.SqliteExtendedErrorCode);
        }
    }

    [Fact]
    public async Task Request_key_is_unique_per_patient_and_clinic_not_globally()
    {
        await using var test = await Database.Create();
        var request = Guid.NewGuid();
        await Insert(test, Valid(request));
        test.Db.Appointments.Add(Valid(request));
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => test.Db.SaveChangesAsync());
        Assert.Equal(2067, Assert.IsType<SqliteException>(error.InnerException).SqliteExtendedErrorCode);
        test.Db.ChangeTracker.Clear();
        test.Db.Patients.Add(new Patient { Id = 43, ClinicId = 101, FullName = "Synthetic unlinked patient" });
        await test.Db.SaveChangesAsync();
        var otherPatient = Valid(request); otherPatient.PatientId = 43;
        await Insert(test, otherPatient);
        var otherClinic = Valid(request); otherClinic.ClinicId = 202; otherClinic.PatientId = 42;
        otherClinic.DoctorProfileId = 32; otherClinic.ServiceId = 61;
        await Insert(test, otherClinic);
        Assert.Equal(3, await test.Db.Appointments.CountAsync());
    }

    [Fact]
    public async Task Touching_half_open_boundaries_are_valid_and_lookup_indexes_do_not_prevent_all_overlaps()
    {
        await using var test = await Database.Create();
        var first = Valid();
        await Insert(test, first);
        var touching = Valid(start: first.OccupiedEndUtc.AddMinutes(5));
        Assert.Equal(first.OccupiedEndUtc, touching.OccupiedStartUtc);
        await Insert(test, touching);
        // This intentionally documents the boundary of the persistence foundation:
        // a distinct request at an overlapping time still needs booking-service validation.
        await Insert(test, Valid(start: first.StartUtc.AddMinutes(10)));
        var sorted = await test.Db.Appointments.AsNoTracking().Where(a => a.ClinicId == 101 && a.StartUtc >= first.StartUtc)
            .OrderBy(a => a.StartUtc).Select(a => a.StartUtc).ToArrayAsync();
        Assert.Equal(new[] { first.StartUtc, first.StartUtc.AddMinutes(10), touching.StartUtc }, sorted);
        Assert.All(sorted, time => Assert.Equal(TimeSpan.Zero, time.Offset));
    }
}
