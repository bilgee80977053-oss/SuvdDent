using DentalApi.Contracts;
using DentalApi.Data;
using DentalApi.Models;
using Microsoft.EntityFrameworkCore;

namespace DentalApi.Services;

public sealed class ClinicLocationService(AppDbContext db, MongoliaLocations locations)
{
    public async Task<ClinicLocationResponse?> Read(int clinicId)
    {
        var c = await db.Clinics.AsNoTracking().SingleOrDefaultAsync(c => c.Id == clinicId);
        return c is null ? null : Response(c);
    }
    private ClinicLocationResponse Response(Clinic c) => new(c.CountryCode, c.ProvinceOrCapitalCode,
        c.SoumOrDistrictCode, c.AddressLine, c.TimeZoneId,
        locations.Suggest(c.CountryCode ?? "", c.ProvinceOrCapitalCode ?? "", c.SoumOrDistrictCode ?? ""),
        c.TimeZoneStatus == TimeZoneStatus.Verified ? c.TimeZoneId : null,
        c.TimeZoneStatus, c.TimeZoneVerifiedAtUtc, c.Version);

    public Task<SchedulingResult> Update(int clinic, int user, UpdateClinicLocationRequest request) =>
        Write(clinic, user, request.Version, request, null);
    public Task<SchedulingResult> Confirm(int clinic, int user, ConfirmClinicTimeZoneRequest request) =>
        Write(clinic, user, request.Version, null, request.TimeZoneId);

    private async Task<SchedulingResult> Write(int clinicId, int userId, Guid version,
        UpdateClinicLocationRequest? location, string? zone)
    {
        if (location is not null && (location.Location is null ||
            !locations.Valid(location.Location.CountryCode, location.Location.ProvinceOrCapitalCode, location.Location.SoumOrDistrictCode) ||
            string.IsNullOrWhiteSpace(location.AddressLine) || location.AddressLine.Length > 500)) return new(400);
        if (location is null && (zone is null || !MongoliaLocations.Supported(zone))) return new(400);
        try
        {
            await using var tx = await DatabaseCompatibility.BeginWriteAsync(db);
            if (!await db.Users.AnyAsync(u => u.Id == userId && u.ClinicId == clinicId && u.IsActive && u.Role == Role.Director))
                return new(403);
            var c = await db.Clinics.SingleOrDefaultAsync(c => c.Id == clinicId);
            if (c is null) return new(404);
            if (c.Version != version) return new(409);
            if (location is not null)
            {
                var input = location.Location;
                var address = location.AddressLine.Trim();
                if (c.CountryCode != input.CountryCode || c.ProvinceOrCapitalCode != input.ProvinceOrCapitalCode ||
                    c.SoumOrDistrictCode != input.SoumOrDistrictCode || c.AddressLine != address)
                {
                    c.CountryCode = input.CountryCode;
                    c.ProvinceOrCapitalCode = input.ProvinceOrCapitalCode;
                    c.SoumOrDistrictCode = input.SoumOrDistrictCode;
                    c.AddressLine = address;
                    c.TimeZoneId = locations.Suggest(input.CountryCode, input.ProvinceOrCapitalCode, input.SoumOrDistrictCode)!;
                    c.TimeZoneStatus = TimeZoneStatus.PendingVerification;
                    c.TimeZoneVerifiedByUserId = null;
                    c.TimeZoneVerifiedClinicId = null;
                    c.TimeZoneVerifiedAtUtc = null;
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(c.AddressLine) ||
                    locations.Suggest(c.CountryCode ?? "", c.ProvinceOrCapitalCode ?? "", c.SoumOrDistrictCode ?? "") != zone)
                    return new(400);
                try { _ = TimeZoneInfo.FindSystemTimeZoneById(zone!); }
                catch (TimeZoneNotFoundException) { return new(400); }
                catch (InvalidTimeZoneException) { return new(400); }
                c.TimeZoneId = zone!;
                c.TimeZoneStatus = TimeZoneStatus.Verified;
                c.TimeZoneVerifiedByUserId = userId;
                c.TimeZoneVerifiedClinicId = clinicId;
                c.TimeZoneVerifiedAtUtc = DateTime.UtcNow;
            }
            c.Version = Guid.NewGuid();
            await db.SaveChangesAsync();
            var response = Response(c);
            await tx.CommitAsync();
            return new(200, response);
        }
        catch (Exception e) when (DatabaseCompatibility.IsConflict(e)) { return new(409); }
    }
}
