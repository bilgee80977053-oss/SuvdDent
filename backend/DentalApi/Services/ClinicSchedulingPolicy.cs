using DentalApi.Data;
using DentalApi.Models;
using Microsoft.EntityFrameworkCore;

namespace DentalApi.Services;

public record ClinicSchedulingDecision(int Status, string? ErrorCode, TimeZoneInfo? TimeZone, Guid? Version);

public sealed class ClinicSchedulingPolicy(AppDbContext db, MongoliaLocations locations)
{
    public const string Unverified = "CLINIC_TIMEZONE_UNVERIFIED";

    // Future matching/booking/rescheduling writers must call this within the SAME
    // write transaction as resource validation and the booking write (SQLite
    // non-deferred; PostgreSQL serializable via DatabaseCompatibility).
    public async Task<ClinicSchedulingDecision> RequireVerifiedAsync(int clinicId, Guid? expectedVersion = null)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Scheduling policy requires the operation's transaction.");
        var clinic = await db.Clinics.AsNoTracking().SingleOrDefaultAsync(c => c.Id == clinicId);
        if (clinic is null || clinic.TimeZoneStatus != TimeZoneStatus.Verified ||
            !locations.Valid(clinic.CountryCode ?? "", clinic.ProvinceOrCapitalCode ?? "", clinic.SoumOrDistrictCode ?? "") ||
            locations.Suggest(clinic.CountryCode!, clinic.ProvinceOrCapitalCode!, clinic.SoumOrDistrictCode!) != clinic.TimeZoneId ||
            string.IsNullOrWhiteSpace(clinic.AddressLine) || clinic.TimeZoneVerifiedAtUtc is null ||
            !await db.Users.AnyAsync(u => u.Id == clinic.TimeZoneVerifiedByUserId && u.ClinicId == clinicId && u.IsActive && u.Role == Role.Director))
            return new(409, Unverified, null, clinic?.Version);
        if (expectedVersion.HasValue && clinic.Version != expectedVersion.Value)
            return new(409, "CLINIC_SCHEDULING_VERSION_CONFLICT", null, clinic.Version);
        try { return new(200, null, TimeZoneInfo.FindSystemTimeZoneById(clinic.TimeZoneId), clinic.Version); }
        catch (TimeZoneNotFoundException) { return new(409, Unverified, null, clinic.Version); }
        catch (InvalidTimeZoneException) { return new(409, Unverified, null, clinic.Version); }
    }
}
