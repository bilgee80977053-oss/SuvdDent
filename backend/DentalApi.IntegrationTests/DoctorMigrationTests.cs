using System.Text.Json;
using DentalApi.Data;
using DentalApi.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace DentalApi.IntegrationTests;

public sealed class DoctorMigrationTests
{
    [Fact]
    public async Task AddDoctorProfiles_preserves_all_existing_tables_data_and_schema()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.GetService<IMigrator>().MigrateAsync("20261026100808_AddPatientUserTenantIntegrity");
        var date = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        await db.Database.ExecuteSqlRawAsync("INSERT INTO Clinics (Id, Name, WorkHours) VALUES (101, 'Synthetic clinic', 'Synthetic')");
        db.Users.Add(new User { Id = 11, ClinicId = 101, FullName = "Synthetic user",
            Email = "synthetic@example.invalid", Phone = "11111111", PasswordHash = "synthetic-only",
            Role = Role.Doctor, IsEmailVerified = true, TokenVersion = 7, CreatedAt = date });
        db.Patients.Add(new Patient { Id = 21, ClinicId = 101, UserId = 11,
            FullName = "Synthetic patient", Phone = "11111111", Allergies = "Synthetic",
            MedicalNotes = "Synthetic note", Address = "Synthetic", DateOfBirth = date.AddYears(-20), CreatedAt = date });
        db.Services.Add(new Service { Id = 31, ClinicId = 101, Name = "Synthetic service", MinPrice = 100, MaxPrice = 200 });
        db.VerificationCodes.Add(new VerificationCode { Id = 41, UserId = 11, Channel = "email",
            Purpose = "registration", CodeHash = "synthetic-only", CreatedAt = date, ExpiresAt = date.AddMinutes(2), FailedAttempts = 2 });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var existingTables = new[] { "Clinics", "Users", "Patients", "Services", "VerificationCodes" };
        async Task<string> SnapshotAsync()
        {
            var state = new List<string>();
            foreach (var table in existingTables)
            {
                state.Add(await QueryAsync(connection, $"SELECT * FROM \"{table}\" ORDER BY Id"));
                state.Add(await QueryAsync(connection, $"PRAGMA table_info('{table}')"));
                state.Add(await QueryAsync(connection, $"PRAGMA foreign_key_list('{table}')"));
                state.Add(await QueryAsync(connection, $"PRAGMA index_list('{table}')"));
            }
            return JsonSerializer.Serialize(state);
        }
        var before = await SnapshotAsync();
        await db.GetService<IMigrator>().MigrateAsync("20261026104238_AddDoctorProfiles");
        Assert.Equal(before, await SnapshotAsync());
        Assert.Empty(await db.DoctorProfiles.ToArrayAsync());
        Assert.Empty(await db.DoctorWorkingHours.ToArrayAsync());
        Assert.Equal("[]", await QueryAsync(connection, "PRAGMA foreign_key_check"));
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check";
        Assert.Equal("ok", await command.ExecuteScalarAsync());
        Assert.EndsWith("_AddDoctorProfiles", (await db.Database.GetAppliedMigrationsAsync()).Last());
        Assert.False(db.Database.HasPendingModelChanges());
    }

    private static async Task<string> QueryAsync(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = await command.ExecuteReaderAsync();
        var rows = new List<object?[]>();
        while (await reader.ReadAsync())
        {
            var row = new object?[reader.FieldCount];
            for (var i = 0; i < row.Length; i++) row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }
        return JsonSerializer.Serialize(rows);
    }
}