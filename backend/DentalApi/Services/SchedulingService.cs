using DentalApi.Contracts;
using DentalApi.Data;
using DentalApi.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DentalApi.Services;

public record SchedulingResult(int Status, object? Data = null);

public class SchedulingService(AppDbContext db)
{
    private IQueryable<DoctorProfile> Doctors(int clinic) => db.DoctorProfiles.Where(d =>
        d.ClinicId == clinic && d.IsActive && d.User.ClinicId == clinic &&
        d.User.IsActive && d.User.Role == Role.Doctor);
    private Task<int?> PatientId(int clinic, int user) => db.Patients.Where(p =>
        p.ClinicId == clinic && p.UserId == user && p.IsActive && db.Users.Any(u =>
            u.Id == user && u.ClinicId == clinic && u.IsActive && u.Role == Role.Patient))
        .Select(p => (int?)p.Id).SingleOrDefaultAsync();

    public Task<SchedulingResult> ReadServices(int clinic, int doctor) =>
        ReadSnapshot(() => ServicesSnapshot(clinic, doctor));

    private async Task<SchedulingResult> ReadSnapshot(Func<Task<SchedulingResult>> read)
    {
        try
        {
            await db.Database.OpenConnectionAsync();
            await using var transaction = ((SqliteConnection)db.Database.GetDbConnection()).BeginTransaction(deferred: true);
            await using var enlisted = await db.Database.UseTransactionAsync(transaction);
            var result = await read();
            await transaction.CommitAsync();
            return result;
        }
        catch (Exception error) when (Conflict(error)) { return new(409); }
    }

    private async Task<SchedulingResult> ServicesSnapshot(int clinic, int doctor)
    {
        var version = await Doctors(clinic).Where(d => d.Id == doctor).Select(d => (Guid?)d.Version).SingleOrDefaultAsync();
        if (version is null) return new(404);
        var items = await db.DoctorServices.AsNoTracking().Where(x => x.ClinicId == clinic && x.DoctorProfileId == doctor)
            .OrderBy(x => x.ServiceId).Select(x => new DoctorServiceInput(x.ServiceId, x.DurationMinutes,
                x.BufferBeforeMinutes, x.BufferAfterMinutes, x.IsActive)).ToArrayAsync();
        return new(200, new DoctorServicesResponse(doctor, version.Value, items));
    }

    public async Task<SchedulingResult> WriteServices(int clinic, int doctor, UpdateDoctorServicesRequest request)
    {
        var items = request.Services;
        if (request.Version == Guid.Empty || items is null || items.Length > 100 ||
            items.Any(x => x is null || x.ServiceId <= 0 || x.DurationMinutes < 1 || x.DurationMinutes > 1440 ||
                x.BufferBeforeMinutes < 0 || x.BufferAfterMinutes < 0 || x.BufferBeforeMinutes > 1439 ||
                x.BufferAfterMinutes > 1439 || (long)x.DurationMinutes + x.BufferBeforeMinutes + x.BufferAfterMinutes > 1440) ||
            items.Select(x => x.ServiceId).Distinct().Count() != items.Length) return new(400);
        try
        {
            // Microsoft.Data.Sqlite begins a non-deferred transaction: the write lock
            // precedes reads/validation. Version CAS also covers profile/hour editors.
            await using var tx = await db.Database.BeginTransactionAsync();
            var profile = await Doctors(clinic).AsNoTracking().SingleOrDefaultAsync(d => d.Id == doctor);
            if (profile is null) return new(404);
            if (profile.Version != request.Version) return new(409);
            var ids = items.Select(x => x.ServiceId).ToArray();
            if (await db.Services.CountAsync(s => s.ClinicId == clinic && ids.Contains(s.Id) && s.IsActive) != ids.Length)
                return new(404);
            var next = Guid.NewGuid();
            if (await Doctors(clinic).Where(d => d.Id == doctor && d.Version == request.Version)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.Version, next).SetProperty(d => d.UpdatedAt, DateTime.UtcNow)) != 1)
                return new(409);
            var existing = await db.DoctorServices.Where(x => x.ClinicId == clinic && x.DoctorProfileId == doctor).ToListAsync();
            // Preserve identities for future appointment references; omissions deactivate.
            foreach (var row in existing) row.IsActive = false;
            foreach (var item in items)
            {
                var row = existing.SingleOrDefault(x => x.ServiceId == item.ServiceId);
                if (row is null)
                {
                    row = new DoctorService { ClinicId = clinic, DoctorProfileId = doctor, ServiceId = item.ServiceId };
                    db.DoctorServices.Add(row);
                }
                row.DurationMinutes = item.DurationMinutes;
                row.BufferBeforeMinutes = item.BufferBeforeMinutes;
                row.BufferAfterMinutes = item.BufferAfterMinutes;
                row.IsActive = item.IsActive;
            }
            await db.SaveChangesAsync();
            // Read the response inside the transaction so Version and list are one snapshot.
            var result = await ServicesSnapshot(clinic, doctor);
            await tx.CommitAsync();
            return result;
        }
        catch (Exception error) when (Conflict(error)) { return new(409); }
    }

    public Task<SchedulingResult> ReadAvailability(int clinic, int user) => ReadSnapshot(async () =>
    {
        var patient = await PatientId(clinic, user);
        return patient is null ? new(404) : new(200, await Availability(clinic, patient.Value));
    });
    private async Task<AvailabilityResponse> Availability(int clinic, int patient)
    {
        var version = await db.PatientAvailabilitySchedules.Where(x => x.ClinicId == clinic && x.PatientId == patient)
            .Select(x => (Guid?)x.Version).SingleOrDefaultAsync() ?? Guid.Empty;
        var items = await db.PatientAvailabilities.AsNoTracking().Where(x => x.ClinicId == clinic && x.PatientId == patient)
            .OrderBy(x => x.StartUtc).Select(x => new AvailabilityInput(x.StartUtc, x.EndUtc)).ToArrayAsync();
        return new(version, items);
    }

    public async Task<SchedulingResult> WriteAvailability(int clinic, int user, UpdateAvailabilityRequest request)
    {
        if (request.Intervals is null || request.Intervals.Length > 100 ||
            request.Intervals.Any(x => x is null || x.StartUtc >= x.EndUtc)) return new(400);
        var items = request.Intervals.OrderBy(x => x.StartUtc).ToArray();
        for (var i = 1; i < items.Length; i++)
            if (items[i].StartUtc < items[i - 1].EndUtc) return new(400);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            var patient = await PatientId(clinic, user);
            if (patient is null) return new(404);
            var current = await db.PatientAvailabilitySchedules.AsNoTracking()
                .SingleOrDefaultAsync(x => x.ClinicId == clinic && x.PatientId == patient);
            if ((current?.Version ?? Guid.Empty) != request.Version) return new(409);
            var next = Guid.NewGuid();
            if (current is null)
                db.PatientAvailabilitySchedules.Add(new() { ClinicId = clinic, PatientId = patient.Value, Version = next });
            else if (await db.PatientAvailabilitySchedules.Where(x => x.ClinicId == clinic &&
                x.PatientId == patient && x.Version == request.Version)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Version, next)) != 1) return new(409);
            await db.PatientAvailabilities.Where(x => x.ClinicId == clinic && x.PatientId == patient).ExecuteDeleteAsync();
            foreach (var item in items) db.PatientAvailabilities.Add(new()
            {
                ClinicId = clinic, PatientId = patient.Value,
                StartUtc = item.StartUtc.ToUniversalTime(), EndUtc = item.EndUtc.ToUniversalTime()
            });
            await db.SaveChangesAsync();
            var result = await Availability(clinic, patient.Value);
            await tx.CommitAsync();
            return new(200, result);
        }
        catch (Exception error) when (Conflict(error)) { return new(409); }
    }

    private static bool Conflict(Exception error) => error is DbUpdateConcurrencyException ||
        error is SqliteException { SqliteErrorCode: 5 or 6 } ||
        error is DbUpdateException { InnerException: SqliteException { SqliteErrorCode: 5 or 6 } } ||
        error is DbUpdateException { InnerException: SqliteException { SqliteExtendedErrorCode: 2067 or 1555 } };
}
