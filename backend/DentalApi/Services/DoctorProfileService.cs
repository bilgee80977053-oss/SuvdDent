using System.Text.RegularExpressions;
using DentalApi.Contracts;
using DentalApi.Data;
using DentalApi.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DentalApi.Services;

public record DoctorWriteResult(int Status, DoctorResponse? Profile = null, string? Error = null);

public class DoctorProfileService(AppDbContext db)
{
    private IQueryable<DoctorProfile> Scope(int clinicId) =>
        db.DoctorProfiles.Where(d => d.ClinicId == clinicId &&
            d.User.ClinicId == clinicId && d.User.IsActive && d.User.Role == Role.Doctor);

    public async Task<DoctorResponse[]> ListAsync(int clinicId, bool management)
    {
        var query = Scope(clinicId);
        if (!management) query = query.Where(d => d.IsActive && d.IsPublished);
        var profiles = await query.AsNoTracking().Include(d => d.User)
            .Include(d => d.WorkingHours).OrderBy(d => d.Id).ToArrayAsync();
        return profiles.Select(ToResponse).ToArray();
    }

    public async Task<DoctorResponse?> ReadAsync(int clinicId, int? id, int? userId, bool catalog)
    {
        var query = Scope(clinicId);
        if (id.HasValue) query = query.Where(d => d.Id == id);
        if (userId.HasValue) query = query.Where(d => d.UserId == userId);
        if (catalog) query = query.Where(d => d.IsActive && d.IsPublished);
        var profile = await query.AsNoTracking().Include(d => d.User)
            .Include(d => d.WorkingHours).SingleOrDefaultAsync();
        return profile is null ? null : ToResponse(profile);
    }

    public async Task<DoctorWriteResult> CreateAsync(int clinicId, CreateDoctorRequest request)
    {
        if (!ValidDetails(request.Profile, clinicId)) return new(400, Error: "Invalid doctor profile.");
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            // The body identifies a target, never grants tenant or role authority.
            var validUser = await db.Users.AnyAsync(u => u.Id == request.UserId &&
                u.ClinicId == clinicId && u.IsActive && u.Role == Role.Doctor);
            if (!validUser) return new(404, Error: "Eligible doctor user not found.");
            if (await db.DoctorProfiles.AnyAsync(d => d.ClinicId == clinicId && d.UserId == request.UserId))
                return new(409, Error: "Doctor profile already exists.");
            var profile = new DoctorProfile
            {
                ClinicId = clinicId, UserId = request.UserId,
                Specialty = request.Profile.Specialty.Trim(),
                PracticeStartYear = request.Profile.PracticeStartYear,
                Biography = request.Profile.Biography?.Trim(),
                PhotoPath = request.Profile.PhotoPath,
                IsPublished = request.IsPublished
            };
            db.DoctorProfiles.Add(profile);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return new(201, await ReadAsync(clinicId, profile.Id, null, false));
        }
        catch (DbUpdateException e) when (IsConflict(e)) { return Conflict(); }
        catch (SqliteException e) when (IsConflict(e)) { return Conflict(); }
    }

    public async Task<DoctorWriteResult> UpdateAsync(int clinicId, int? id, int? userId,
        Guid version, DoctorDetails? details, bool? published = null, bool? active = null,
        WorkingHoursInput[]? hours = null)
    {
        if (version == Guid.Empty || (details is not null && !ValidDetails(details, clinicId)) ||
            (hours is not null && !ValidHours(hours)))
            return new(400, Error: "Invalid profile, version, or working hours.");
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            var query = Scope(clinicId);
            if (id.HasValue) query = query.Where(d => d.Id == id);
            if (userId.HasValue) query = query.Where(d => d.UserId == userId && d.IsActive);
            var target = await query.AsNoTracking().Select(d => new { d.Id, d.Version }).SingleOrDefaultAsync();
            if (target is null) return new(404, Error: "Doctor profile not found.");
            if (target.Version != version) return Conflict();

            // All profile and schedule writes compete on the same version.
            // CAS plus a single transaction prevents stale schedules replacing
            // newer schedules, even across application processes.
            var nextVersion = Guid.NewGuid();
            var changed = await db.DoctorProfiles.Where(d => d.Id == target.Id &&
                    d.ClinicId == clinicId && d.Version == version)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(d => d.Version, nextVersion)
                    .SetProperty(d => d.UpdatedAt, DateTime.UtcNow));
            if (changed != 1) return Conflict();

            if (details is not null)
                await db.DoctorProfiles.Where(d => d.Id == target.Id && d.ClinicId == clinicId)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(d => d.Specialty, details.Specialty.Trim())
                        .SetProperty(d => d.PracticeStartYear, details.PracticeStartYear)
                        .SetProperty(d => d.Biography, details.Biography == null ? null : details.Biography.Trim())
                        .SetProperty(d => d.PhotoPath, details.PhotoPath));
            if (published.HasValue && active.HasValue)
                await db.DoctorProfiles.Where(d => d.Id == target.Id && d.ClinicId == clinicId)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(d => d.IsPublished, published.Value)
                        .SetProperty(d => d.IsActive, active.Value));
            if (hours is not null)
            {
                await db.DoctorWorkingHours.Where(h => h.DoctorProfileId == target.Id && h.ClinicId == clinicId)
                    .ExecuteDeleteAsync();
                db.DoctorWorkingHours.AddRange(hours.Select(h => new DoctorWorkingHours
                {
                    ClinicId = clinicId, DoctorProfileId = target.Id,
                    DayOfWeek = h.DayOfWeek, StartMinute = h.StartMinute, EndMinute = h.EndMinute
                }));
                await db.SaveChangesAsync();
            }
            await transaction.CommitAsync();
            return new(200, await ReadAsync(clinicId, target.Id, null, false));
        }
        catch (DbUpdateConcurrencyException) { return Conflict(); }
        catch (DbUpdateException e) when (IsConflict(e)) { return Conflict(); }
        catch (SqliteException e) when (IsConflict(e)) { return Conflict(); }
    }

    private static bool IsConflict(Exception error)
    {
        var sqlite = error as SqliteException ?? error.InnerException as SqliteException;
        return sqlite is not null && (sqlite.SqliteErrorCode is 5 or 6 || sqlite.SqliteExtendedErrorCode == 2067);
    }

    private static DoctorWriteResult Conflict() =>
        new(409, Error: "Profile changed or is busy. Reload it before retrying.");

    private static bool ValidDetails(DoctorDetails? details, int clinicId) =>
        details is not null && !string.IsNullOrWhiteSpace(details.Specialty) &&
        details.Specialty.Length <= 200 &&
        details.PracticeStartYear >= 1900 && details.PracticeStartYear <= DateTime.UtcNow.Year &&
        (details.Biography?.Length ?? 0) <= 4000 &&
        (details.PhotoPath is null || Regex.IsMatch(details.PhotoPath,
            @"\A/uploads/doctors/" + clinicId + @"/[A-Za-z0-9_-]+[.](png|jpg|jpeg|webp)\z",
            RegexOptions.CultureInvariant));

    private static bool ValidHours(WorkingHoursInput[] hours)
    {
        if (hours.Length > 70 || hours.Any(h => h is null || h.DayOfWeek < 0 || h.DayOfWeek > 6 ||
            h.StartMinute < 0 || h.EndMinute > 1440 || h.StartMinute >= h.EndMinute))
            return false;
        foreach (var group in hours.GroupBy(h => h.DayOfWeek))
        {
            var ordered = group.OrderBy(h => h.StartMinute).ToArray();
            for (var index = 1; index < ordered.Length; index++)
                if (ordered[index].StartMinute < ordered[index - 1].EndMinute) return false;
        }
        return true;
    }

    private static DoctorResponse ToResponse(DoctorProfile profile) => new(
        profile.Id, profile.User.FullName, profile.Specialty, profile.PracticeStartYear,
        profile.Biography, profile.PhotoPath, profile.IsPublished, profile.IsActive, profile.Version,
        profile.WorkingHours.OrderBy(h => h.DayOfWeek).ThenBy(h => h.StartMinute)
            .Select(h => new WorkingHoursInput(h.DayOfWeek, h.StartMinute, h.EndMinute)).ToArray());
}