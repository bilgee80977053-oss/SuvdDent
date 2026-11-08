using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DentalApi.Contracts;
using DentalApi.Data;
using DentalApi.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DentalApi.Services;

public sealed class AppointmentService(AppDbContext db, ClinicSchedulingPolicy policy, TimeProvider clock,
    IAppointmentConfirmationNotifications notifications)
{
    private IQueryable<DoctorProfile> Doctors(int clinic, int doctor) => db.DoctorProfiles.Where(d =>
        d.ClinicId == clinic && d.Id == doctor && d.IsActive && d.IsPublished && d.User.ClinicId == clinic &&
        d.User.IsActive && d.User.Role == Role.Doctor);
    public async Task<(int Status, object? Data)> Options(int clinic, int doctor, int service)
    {
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            var zone = await policy.RequireVerifiedAsync(clinic);
            if (zone.Status != 200) return (409, new { errorCode = zone.ErrorCode });
            var profile = await Doctors(clinic, doctor).AsNoTracking().SingleOrDefaultAsync();
            var selected = await db.DoctorServices.AsNoTracking().SingleOrDefaultAsync(x => x.ClinicId == clinic &&
                x.DoctorProfileId == doctor && x.ServiceId == service && x.IsActive);
            if (profile is null || selected is null || !await db.Services.AnyAsync(s => s.ClinicId == clinic && s.Id == service && s.IsActive)) return (404, null);
            var hours = await db.DoctorWorkingHours.Where(h => h.ClinicId == clinic && h.DoctorProfileId == doctor)
                .OrderBy(h => h.DayOfWeek).ThenBy(h => h.StartMinute)
                .Select(h => new WorkingHoursInput(h.DayOfWeek, h.StartMinute, h.EndMinute)).ToArrayAsync();
            var result = new BookingOptionsResponse(clinic, doctor, service, zone.Version!.Value, profile.Version,
                zone.TimeZone!.Id, selected.DurationMinutes, selected.BufferBeforeMinutes, selected.BufferAfterMinutes, hours);
            await tx.CommitAsync(); return (200, result);
        }
        catch (Exception e) when (Contention(e)) { return (409, new { errorCode = "BOOKING_BUSY" }); }
    }

    public async Task<BookingResult> Book(int accountId, int tokenVersion, int clinic, Guid key, BookAppointmentRequest request)
    {
        if (key == Guid.Empty || clinic <= 0 || request.DoctorProfileId <= 0 || request.ServiceId <= 0 || request.DoctorVersion == Guid.Empty)
            return new(400, ErrorCode: "INVALID_BOOKING_REQUEST");
        var start = request.StartUtc.ToUniversalTime();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        { clinic, request.DoctorProfileId, request.ServiceId, ticks = start.UtcDateTime.Ticks,
            request.ClinicVersion, request.DoctorVersion, request.ConsentToCreateNewPatient }))));
        try
        {
            // SQLite's non-deferred transaction obtains the writer reservation BEFORE
            // checking overlap. Separate requests/processes using this DB serialize.
            await using var tx = await db.Database.BeginTransactionAsync();
            var account = await db.PlatformAccounts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == accountId &&
                a.IsActive && a.TokenVersion == tokenVersion && (a.IsEmailVerified || a.IsPhoneVerified));
            if (account is null) return new(401);
            var prior = await db.Appointments.AsNoTracking().SingleOrDefaultAsync(a => a.PlatformAccountId == accountId && a.IdempotencyKey == key);
            if (prior is not null) return prior.RequestHash == hash ? new(200, Response(prior)) : new(409, ErrorCode: "IDEMPOTENCY_KEY_REUSED");
            var now = clock.GetUtcNow();
            if (start <= now || start > now.AddDays(365) || start.UtcDateTime.Ticks % TimeSpan.TicksPerMinute != 0)
                return new(400, ErrorCode: "INVALID_START_TIME");
            var zone = await policy.RequireVerifiedAsync(clinic, request.ClinicVersion);
            if (zone.Status != 200) return new(409, ErrorCode: zone.ErrorCode);
            var doctor = await Doctors(clinic, request.DoctorProfileId).AsNoTracking().Include(d => d.User).SingleOrDefaultAsync();
            var service = await db.Services.AsNoTracking().SingleOrDefaultAsync(s => s.Id == request.ServiceId && s.ClinicId == clinic && s.IsActive);
            var match = await db.DoctorServices.AsNoTracking().SingleOrDefaultAsync(s => s.ClinicId == clinic &&
                s.DoctorProfileId == request.DoctorProfileId && s.ServiceId == request.ServiceId && s.IsActive);
            if (doctor is null || service is null || match is null) return new(404, ErrorCode: "BOOKABLE_RESOURCE_NOT_FOUND");
            if (doctor.Version != request.DoctorVersion) return new(409, ErrorCode: "DOCTOR_SCHEDULING_VERSION_CONFLICT");
            var end = start.AddMinutes(match.DurationMinutes);
            var occupiedStart = start.AddMinutes(-match.BufferBeforeMinutes);
            var occupiedEnd = end.AddMinutes(match.BufferAfterMinutes);
            var hours = await db.DoctorWorkingHours.AsNoTracking().Where(h => h.ClinicId == clinic && h.DoctorProfileId == doctor.Id).ToArrayAsync();
            if (!WithinHours(occupiedStart, occupiedEnd, zone.TimeZone!, hours)) return new(409, ErrorCode: "OUTSIDE_DOCTOR_WORKING_HOURS");
            if (await db.Appointments.AnyAsync(a => a.ClinicId == clinic && a.DoctorProfileId == doctor.Id && a.Status == AppointmentStatus.Confirmed &&
                a.OccupiedStartUtc < occupiedEnd && occupiedStart < a.OccupiedEndUtc)) return new(409, ErrorCode: "DOCTOR_TIME_CONFLICT");
            if (await db.Appointments.AnyAsync(a => a.PlatformAccountId == accountId && a.Status == AppointmentStatus.Confirmed && a.StartUtc < end && start < a.EndUtc))
                return new(409, ErrorCode: "ACCOUNT_TIME_CONFLICT");
            var link = await db.ClinicPatientLinks.SingleOrDefaultAsync(l => l.PlatformAccountId == accountId && l.ClinicId == clinic);
            bool createdPatient = link is null;
            if (link is null)
            {
                if (!request.ConsentToCreateNewPatient) return new(409, ErrorCode: "PATIENT_CREATION_CONSENT_REQUIRED");
                // Never search existing cards by name, phone or email. This is a NEW
                // local card, owned by the authenticated account, with explicit consent.
                var patient = new Patient { ClinicId = clinic, UserId = null, FullName = account.FullName,
                    Phone = account.IsPhoneVerified ? account.Phone ?? "" : "", CreatedAt = now.UtcDateTime };
                db.Patients.Add(patient); await db.SaveChangesAsync();
                link = new ClinicPatientLink { PlatformAccountId = accountId, ClinicId = clinic, PatientId = patient.Id, CreatedAt = now.UtcDateTime };
                db.ClinicPatientLinks.Add(link); await db.SaveChangesAsync();
            }
            else if (!await db.Patients.AnyAsync(p => p.Id == link.PatientId && p.ClinicId == clinic && p.IsActive))
                return new(409, ErrorCode: "LOCAL_PATIENT_INACTIVE");
            var appointment = new Appointment
            {
                ClinicId = clinic, DoctorProfileId = doctor.Id, ServiceId = service.Id, PatientId = link.PatientId,
                PlatformAccountId = accountId, StartUtc = start, EndUtc = end, OccupiedStartUtc = occupiedStart, OccupiedEndUtc = occupiedEnd,
                DurationMinutesSnapshot = match.DurationMinutes, BufferBeforeMinutesSnapshot = match.BufferBeforeMinutes,
                BufferAfterMinutesSnapshot = match.BufferAfterMinutes, TimeZoneIdSnapshot = zone.TimeZone!.Id,
                DoctorNameSnapshot = doctor.User.FullName, ServiceNameSnapshot = service.Name,
                MinPriceSnapshot = service.MinPrice, MaxPriceSnapshot = service.MaxPrice,
                ClinicVersionSnapshot = zone.Version!.Value, DoctorVersionSnapshot = doctor.Version,
                IdempotencyKey = key, RequestHash = hash, CreatedAtUtc = now.UtcDateTime,
                PatientCreationConsentAtUtc = createdPatient ? now.UtcDateTime : null,
                PatientCreationConsentVersion = createdPatient ? "new-local-patient-v1" : null
            };
            db.Appointments.Add(appointment);
            await db.SaveChangesAsync();
            notifications.Record(appointment, now.UtcDateTime);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return new(201, Response(appointment));
        }
        catch (Exception e) when (Contention(e)) { return new(409, ErrorCode: "BOOKING_BUSY"); }
        catch (DbUpdateException e) when (e.InnerException is SqliteException { SqliteExtendedErrorCode: 1811 or 2067 or 1555 })
        { return new(409, ErrorCode: "BOOKING_CONFLICT"); }
    }
    private static bool WithinHours(DateTimeOffset start, DateTimeOffset end, TimeZoneInfo zone, DoctorWorkingHours[] hours)
    {
        // Cover every occupied UTC minute, including buffers, using clinic-local day
        // and minute. No server timezone, rounding, or assumed fixed UTC offset.
        for (var t = start; t < end; t = t.AddMinutes(1))
        {
            var local = TimeZoneInfo.ConvertTime(t, zone);
            var minute = local.Hour * 60 + local.Minute;
            if (!hours.Any(h => h.DayOfWeek == (int)local.DayOfWeek && h.StartMinute <= minute && minute < h.EndMinute)) return false;
        }
        return true;
    }
    private static bool Contention(Exception e) => e is SqliteException { SqliteErrorCode: 5 or 6 } ||
        e is DbUpdateException { InnerException: SqliteException { SqliteErrorCode: 5 or 6 } };
    private static AppointmentResponse Response(Appointment a) => new(a.Id, a.ClinicId, a.DoctorProfileId, a.ServiceId,
        a.Status, a.StartUtc, a.EndUtc, a.TimeZoneIdSnapshot, a.DoctorNameSnapshot, a.ServiceNameSnapshot,
        a.DurationMinutesSnapshot, a.BufferBeforeMinutesSnapshot, a.BufferAfterMinutesSnapshot,
        a.MinPriceSnapshot, a.MaxPriceSnapshot, DateTime.SpecifyKind(a.CreatedAtUtc, DateTimeKind.Utc));

    public async Task<AppointmentResponse[]> Mine(int account, int skip, int take) =>
        (await db.Appointments.AsNoTracking().Where(a => a.PlatformAccountId == account)
            .OrderBy(a => a.StartUtc).ThenBy(a => a.Id).Skip(skip).Take(take).ToArrayAsync()).Select(Response).ToArray();
    public async Task<AppointmentResponse?> Own(int account, int id)
    {
        var value = await db.Appointments.AsNoTracking().SingleOrDefaultAsync(a => a.PlatformAccountId == account && a.Id == id);
        return value is null ? null : Response(value);
    }
    public async Task<AppointmentResponse[]> Staff(int clinic, int user, bool doctorOnly, int skip, int take) =>
        (await db.Appointments.AsNoTracking().Where(a => a.ClinicId == clinic && (!doctorOnly || db.DoctorProfiles.Any(d =>
            d.Id == a.DoctorProfileId && d.ClinicId == clinic && d.UserId == user)))
            .OrderBy(a => a.StartUtc).ThenBy(a => a.Id).Skip(skip).Take(take).ToArrayAsync()).Select(Response).ToArray();
}
