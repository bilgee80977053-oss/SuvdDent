using DentalApi.Contracts;
using DentalApi.Data;
using DentalApi.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DentalApi.Services;

public sealed record CatalogResult<T>(int Status, T[]? Items = null);

// Catalog eligibility is not a promise that any particular time is free.
public sealed class PlatformCatalogService(AppDbContext db, ClinicSchedulingPolicy policy)
{
    private IQueryable<DoctorProfile> Doctors(int? clinic = null) => db.DoctorProfiles.AsNoTracking().Where(d =>
        (!clinic.HasValue || d.ClinicId == clinic.Value) && d.IsActive && d.IsPublished &&
        d.User.ClinicId == d.ClinicId && d.User.IsActive && d.User.Role == Role.Doctor &&
        db.DoctorWorkingHours.Any(h => h.ClinicId == d.ClinicId && h.DoctorProfileId == d.Id) &&
        db.DoctorServices.Any(m => m.ClinicId == d.ClinicId && m.DoctorProfileId == d.Id && m.IsActive &&
            db.Services.Any(s => s.ClinicId == d.ClinicId && s.Id == m.ServiceId && s.IsActive)));

    private async Task<CatalogResult<T>> Snapshot<T>(Func<Task<CatalogResult<T>>> query)
    {
        try
        {
            await db.Database.OpenConnectionAsync();
            await using var tx = ((SqliteConnection)db.Database.GetDbConnection()).BeginTransaction(deferred: true);
            await using var enlisted = await db.Database.UseTransactionAsync(tx);
            var result = await query();
            await tx.CommitAsync();
            return result;
        }
        catch (SqliteException e) when (e.SqliteErrorCode is 5 or 6) { return new(409); }
    }

    public Task<CatalogResult<PlatformClinicDto>> Clinics() => Snapshot(async () =>
    {
        var eligible = Doctors(null).Select(d => d.ClinicId);
        var rows = await db.Clinics.AsNoTracking().Where(c => eligible.Contains(c.Id))
            .OrderBy(c => c.Name).ThenBy(c => c.Id).Select(c => new PlatformClinicDto(c.Id,c.Name,c.TimeZoneId)).ToArrayAsync();
        var result = new List<PlatformClinicDto>();
        foreach (var row in rows)
            if ((await policy.RequireVerifiedAsync(row.Id)).Status == 200) result.Add(row);
        return new CatalogResult<PlatformClinicDto>(200,result.ToArray());
    });

    public Task<CatalogResult<PlatformDoctorDto>> ClinicDoctors(int clinic) => clinic <= 0
        ? Task.FromResult(new CatalogResult<PlatformDoctorDto>(400)) : Snapshot(async () =>
        {
            if ((await policy.RequireVerifiedAsync(clinic)).Status != 200) return new CatalogResult<PlatformDoctorDto>(404);
            var rows = await Doctors(clinic).OrderBy(d => d.User.FullName).ThenBy(d => d.Id)
                .Select(d => new PlatformDoctorDto(d.Id,d.User.FullName,d.Specialty,d.PracticeStartYear,d.Biography,d.PhotoPath)).ToArrayAsync();
            return new(200,rows);
        });

    public Task<CatalogResult<PlatformDoctorServiceDto>> DoctorServices(int clinic,int doctor) => clinic <= 0 || doctor <= 0
        ? Task.FromResult(new CatalogResult<PlatformDoctorServiceDto>(400)) : Snapshot(async () =>
        {
            if ((await policy.RequireVerifiedAsync(clinic)).Status != 200 || !await Doctors(clinic).AnyAsync(d => d.Id == doctor))
                return new CatalogResult<PlatformDoctorServiceDto>(404);
            var rows = await (from m in db.DoctorServices.AsNoTracking()
                join s in db.Services.AsNoTracking() on new { m.ClinicId, Id=m.ServiceId } equals new { s.ClinicId,s.Id }
                where m.ClinicId == clinic && m.DoctorProfileId == doctor && m.IsActive && s.IsActive
                orderby s.Name,s.Id
                select new PlatformDoctorServiceDto(s.Id,s.Name,s.Description,s.MinPrice,s.MaxPrice,
                    m.DurationMinutes,m.BufferBeforeMinutes,m.BufferAfterMinutes)).ToArrayAsync();
            return new(200,rows);
        });
}
