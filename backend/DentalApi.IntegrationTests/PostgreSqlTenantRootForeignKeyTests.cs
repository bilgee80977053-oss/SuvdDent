using DentalApi.Data;
using DentalApi.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace DentalApi.IntegrationTests;

[Collection("Real PostgreSQL"), Trait("Category", "PostgreSqlIntegration")]
public sealed class PostgreSqlTenantRootForeignKeyTests
{
    private const string RequiredMigration = "20261125115303_AddPostgreSqlClinicTenantRootForeignKeys";

    [PostgreSqlFact]
    public async Task Root_foreign_keys_enforce_tenant_references_and_allow_staged_clinic_verification()
    {
        var connectionString = PostgreSqlTestTarget.FromEnvironment();
        await PostgreSqlTestTarget.VerifyAsync(connectionString);
        await using var db = new PostgreSqlAppDbContext(
            new DbContextOptionsBuilder<PostgreSqlAppDbContext>().UseNpgsql(connectionString).Options);
        if (!(await db.Database.GetAppliedMigrationsAsync()).Contains(RequiredMigration))
            throw new InvalidOperationException(
                "The tenant-root migration must be applied to the approved disposable PostgreSQL test database by an authorized operator. This test will not apply migrations.");

        await using var transaction = await db.Database.BeginTransactionAsync();
        var clinicA = new Clinic { Name = "Synthetic tenant-root A " + Guid.NewGuid().ToString("N") };
        var clinicB = new Clinic { Name = "Synthetic tenant-root B " + Guid.NewGuid().ToString("N") };
        db.Clinics.AddRange(clinicA, clinicB);
        await db.SaveChangesAsync();

        var director = new User
        {
            ClinicId = clinicA.Id,
            FullName = "Synthetic director",
            PasswordHash = "synthetic-only",
            Role = Role.Director
        };
        var foreignUser = new User
        {
            ClinicId = clinicB.Id,
            FullName = "Synthetic foreign user",
            PasswordHash = "synthetic-only",
            Role = Role.Patient
        };
        db.Users.AddRange(director, foreignUser);
        await db.SaveChangesAsync();

        var unlinkedPatient = new Patient
        {
            ClinicId = clinicA.Id,
            UserId = null,
            FullName = "Synthetic clinic-managed patient"
        };
        db.Patients.Add(unlinkedPatient);
        db.Services.Add(new Service { ClinicId = clinicA.Id, Name = "Synthetic service" });
        await db.SaveChangesAsync();

        clinicA.CountryCode = "MN";
        clinicA.ProvinceOrCapitalCode = "11";
        clinicA.SoumOrDistrictCode = "1101";
        clinicA.AddressLine = "Synthetic address";
        clinicA.TimeZoneStatus = TimeZoneStatus.Verified;
        clinicA.TimeZoneId = "Asia/Ulaanbaatar";
        clinicA.TimeZoneVerifiedClinicId = clinicA.Id;
        clinicA.TimeZoneVerifiedByUserId = director.Id;
        clinicA.TimeZoneVerifiedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();

        await AssertForeignKeyRejectedAsync(transaction, db, "bad_user",
            new User { ClinicId = int.MaxValue, FullName = "Invalid clinic user", PasswordHash = "synthetic-only" });
        await AssertForeignKeyRejectedAsync(transaction, db, "bad_service",
            new Service { ClinicId = int.MaxValue, Name = "Invalid clinic service" });
        await AssertForeignKeyRejectedAsync(transaction, db, "bad_patient",
            new Patient { ClinicId = int.MaxValue, UserId = null, FullName = "Invalid clinic patient" });
        await AssertForeignKeyRejectedAsync(transaction, db, "cross_clinic_patient",
            new Patient { ClinicId = clinicA.Id, UserId = foreignUser.Id, FullName = "Cross-clinic patient" });

        await transaction.CreateSavepointAsync("delete_clinic");
        var deleteError = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlRawAsync("DELETE FROM public.\"Clinics\" WHERE \"Id\" = {0}", clinicA.Id));
        Assert.Equal("23001", deleteError.SqlState);
        await transaction.RollbackToSavepointAsync("delete_clinic");
        Assert.True(await db.Clinics.AnyAsync(clinic => clinic.Id == clinicA.Id));
        Assert.Null(await db.Patients.Where(patient => patient.Id == unlinkedPatient.Id)
            .Select(patient => patient.UserId).SingleAsync());

        await transaction.RollbackAsync();
    }

    private static async Task AssertForeignKeyRejectedAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        PostgreSqlAppDbContext db,
        string savepoint,
        object entity)
    {
        await transaction.CreateSavepointAsync(savepoint);
        db.Add(entity);
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("23503", Assert.IsType<PostgresException>(error.GetBaseException()).SqlState);
        await transaction.RollbackToSavepointAsync(savepoint);
        db.ChangeTracker.Clear();
    }
}
