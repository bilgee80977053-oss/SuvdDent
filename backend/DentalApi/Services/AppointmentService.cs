using System.Globalization;
using System.Text.RegularExpressions;
using DentalApi.Contracts;
using DentalApi.Data;
using DentalApi.Models;
using Microsoft.EntityFrameworkCore;

namespace DentalApi.Services;

public record AppointmentResult(int Status, object Data);

public sealed class AppointmentService(AppDbContext db, ClinicSchedulingPolicy policy, TimeProvider clock)
{
    private static AppointmentResult Error(int status, string message) => new(status, new { message });
    private static AppointmentResult Conflict() => Error(409, "Сонгосон цаг боломжгүй эсвэл захиалга давхацлаа.");
    private IQueryable<DoctorProfile> Doctors(int clinic) => db.DoctorProfiles.AsNoTracking().Where(d =>
        d.ClinicId == clinic && d.IsActive && d.IsPublished && d.User.ClinicId == clinic &&
        d.User.IsActive && d.User.Role == Role.Doctor);
    private IQueryable<Patient> Patients(int clinic, int user) => db.Patients.AsNoTracking().Where(p =>
        p.ClinicId == clinic && p.UserId == user && p.IsActive && db.Users.Any(u =>
            u.Id == user && u.ClinicId == clinic && u.IsActive && u.Role == Role.Patient));
    private IQueryable<Appointment> Own(int clinic, int patient) => db.Appointments.AsNoTracking()
        .Where(a => a.ClinicId == clinic && a.PatientId == patient);
    private static AppointmentResponse Response(Appointment a) => new(a.Id, a.DoctorProfileId, a.ServiceId,
        a.StartUtc.ToUniversalTime(), a.EndUtc.ToUniversalTime(), a.OccupiedStartUtc.ToUniversalTime(),
        a.OccupiedEndUtc.ToUniversalTime(), a.ClinicTimeZoneIdSnapshot, a.Status.ToString(), a.CreatedAtUtc.ToUniversalTime());

    public async Task<AppointmentResult> Services(int clinic) => new(200, await db.Services.AsNoTracking()
        .Where(s => s.ClinicId == clinic && s.IsActive).OrderBy(s => s.Id)
        .Select(s => new BookingServiceResponse(s.Id, s.Name, s.Description, s.ImagePath, s.MinPrice, s.MaxPrice)).ToArrayAsync());

    public async Task<AppointmentResult> DoctorsForService(int clinic, int service)
    {
        if (service <= 0) return Error(400, "Үйлчилгээний дугаар буруу байна.");
        if (!await db.Services.AnyAsync(s => s.ClinicId == clinic && s.Id == service && s.IsActive))
            return Error(404, "Үйлчилгээ олдсонгүй.");
        return new(200, await Doctors(clinic).Where(d => db.DoctorServices.Any(s => s.ClinicId == clinic &&
                s.DoctorProfileId == d.Id && s.ServiceId == service && s.IsActive)).OrderBy(d => d.Id)
            .Select(d => new BookingDoctorResponse(d.Id, d.User.FullName, d.Specialty, d.PracticeStartYear, d.Biography, d.PhotoPath)).ToArrayAsync());
    }

    private static bool ParseStart(string? text, out DateTimeOffset start)
    {
        start = default;
        return text is not null && text.Length <= 40 &&
            Regex.IsMatch(text, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$") &&
            DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out start);
    }

    private bool InWindow(DateOnly date, TimeZoneInfo zone, DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        return date >= today && date <= today.AddDays(30);
    }

    // Treatment starts lie on a local midnight-anchored 15 minute grid. The whole
    // buffered interval must fit a single working-hours interval. Never resolve DST by guessing.
    internal static AppointmentSlot? Candidate(DateOnly date, int minute, TimeZoneInfo zone,
        DoctorService config, DoctorWorkingHours[] hours, DateTimeOffset now)
    {
        if (minute < 0 || minute >= 1440 || minute % 15 != 0 || config.DurationMinutes is < 1 or > 1440 ||
            config.BufferBeforeMinutes is < 0 or > 1439 || config.BufferAfterMinutes is < 0 or > 1439 ||
            (long)config.DurationMinutes + config.BufferBeforeMinutes + config.BufferAfterMinutes > 1440) return null;
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified).AddMinutes(minute);
        var end = local.AddMinutes(config.DurationMinutes);
        var before = local.AddMinutes(-config.BufferBeforeMinutes);
        var after = end.AddMinutes(config.BufferAfterMinutes);
        var weekday = (int)date.DayOfWeek;
        if (!hours.Any(h => h.DayOfWeek == weekday && h.StartMinute >= 0 && h.EndMinute <= 1440 &&
            h.StartMinute < h.EndMinute && h.StartMinute <= minute - config.BufferBeforeMinutes &&
            minute + config.DurationMinutes + config.BufferAfterMinutes <= h.EndMinute)) return null;
        if (new[] { local, end, before, after }.Any(t => zone.IsInvalidTime(t) || zone.IsAmbiguousTime(t))) return null;
        var startUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone));
        var endUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(end, zone));
        // Also reject intervals crossing an offset transition: persisted snapshots are elapsed minutes.
        if (startUtc <= now || endUtc - startUtc != TimeSpan.FromMinutes(config.DurationMinutes) ||
            startUtc - new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(before, zone)) != TimeSpan.FromMinutes(config.BufferBeforeMinutes) ||
            new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(after, zone)) - endUtc != TimeSpan.FromMinutes(config.BufferAfterMinutes)) return null;
        return new(startUtc, endUtc);
    }

    private async Task<DoctorService?> Configuration(int clinic, int doctor, int service) =>
        await db.DoctorServices.AsNoTracking().SingleOrDefaultAsync(s => s.ClinicId == clinic &&
            s.DoctorProfileId == doctor && s.ServiceId == service && s.IsActive &&
            db.Services.Any(v => v.ClinicId == clinic && v.Id == service && v.IsActive) &&
            Doctors(clinic).Any(d => d.Id == doctor));

    private Task<DoctorWorkingHours[]> Hours(int clinic, int doctor) => db.DoctorWorkingHours.AsNoTracking()
        .Where(h => h.ClinicId == clinic && h.DoctorProfileId == doctor).ToArrayAsync();

    private Task<bool> Overlap(int clinic, int patient, int doctor, AppointmentSlot slot, DoctorService config)
    {
        var start = slot.StartUtc.AddMinutes(-config.BufferBeforeMinutes);
        var end = slot.EndUtc.AddMinutes(config.BufferAfterMinutes);
        return db.Appointments.AnyAsync(a => a.ClinicId == clinic && a.Status == AppointmentStatus.Confirmed &&
            ((a.DoctorProfileId == doctor && a.OccupiedStartUtc < end && start < a.OccupiedEndUtc) ||
             (a.PatientId == patient && a.StartUtc < slot.EndUtc && slot.StartUtc < a.EndUtc)));
    }

    public async Task<AppointmentResult> Slots(int clinic, int user, int doctor, int service, string? dateText)
    {
        if (doctor <= 0 || service <= 0 || !DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date)) return Error(400, "Огноо эсвэл дугаар буруу байна.");
        try
        {
            // Uses the same provider-aware transaction and policy as booking. No writes occur.
            await using var tx = await DatabaseCompatibility.BeginWriteAsync(db);
            var patient = await Patients(clinic, user).Select(p => (int?)p.Id).SingleOrDefaultAsync();
            if (patient is null) return Error(404, "Өвчтөний бүртгэл олдсонгүй.");
            var decision = await policy.RequireVerifiedAsync(clinic);
            if (decision.TimeZone is null) return Error(409, "Эмнэлгийн цагийн бүсийг баталгаажуулах шаардлагатай.");
            var now = clock.GetUtcNow();
            if (!InWindow(date, decision.TimeZone, now)) return Error(400, "Дараагийн 30 хоногийн огноог сонгоно уу.");
            var config = await Configuration(clinic, doctor, service);
            if (config is null) return Error(404, "Эмч эсвэл үйлчилгээ олдсонгүй.");
            var hours = await Hours(clinic, doctor);
            var candidates = new List<AppointmentSlot>();
            for (var minute = 0; minute < 1440; minute += 15)
            {
                var slot = Candidate(date, minute, decision.TimeZone, config, hours, now);
                if (slot is not null) candidates.Add(slot);
            }
            var available = new List<AppointmentSlot>();
            if (candidates.Count > 0)
            {
                var first = candidates.Min(s => s.StartUtc).AddMinutes(-config.BufferBeforeMinutes);
                var last = candidates.Max(s => s.EndUtc).AddMinutes(config.BufferAfterMinutes);
                var blocked = await db.Appointments.AsNoTracking().Where(a => a.ClinicId == clinic &&
                    a.Status == AppointmentStatus.Confirmed && (a.DoctorProfileId == doctor || a.PatientId == patient.Value) &&
                    a.OccupiedStartUtc < last && first < a.OccupiedEndUtc)
                    .Select(a => new { a.DoctorProfileId, a.PatientId, a.StartUtc, a.EndUtc, a.OccupiedStartUtc, a.OccupiedEndUtc }).ToArrayAsync();
                available.AddRange(candidates.Where(s => !blocked.Any(a =>
                    (a.DoctorProfileId == doctor && a.OccupiedStartUtc < s.EndUtc.AddMinutes(config.BufferAfterMinutes) &&
                        s.StartUtc.AddMinutes(-config.BufferBeforeMinutes) < a.OccupiedEndUtc) ||
                    (a.PatientId == patient.Value && a.StartUtc < s.EndUtc && s.StartUtc < a.EndUtc))));
            }
            await tx.CommitAsync();
            return new(200, new AppointmentSlotsResponse(date.ToString("yyyy-MM-dd"), decision.TimeZone.Id, available.ToArray()));
        }
        catch (Exception e) when (DatabaseCompatibility.IsConflict(e)) { return Conflict(); }
    }

    private static AppointmentResult Replay(Appointment old, CreateAppointmentRequest request, DateTimeOffset start) =>
        old.DoctorProfileId == request.DoctorProfileId && old.ServiceId == request.ServiceId && old.StartUtc == start
            ? new(200, Response(old)) : Error(409, "Хүсэлтийн дугаарыг өөр захиалгад ашигласан байна.");

    public async Task<AppointmentResult> Create(int clinic, int user, CreateAppointmentRequest request)
    {
        if (request.DoctorProfileId <= 0 || request.ServiceId <= 0 || request.RequestId == Guid.Empty ||
            !ParseStart(request.StartUtc, out var start)) return Error(400, "Захиалгын мэдээлэл буруу байна.");
        start = start.ToUniversalTime();
        int? validatedPatient = null;
        try
        {
            await using var tx = await DatabaseCompatibility.BeginWriteAsync(db);
            if (!await db.Users.AnyAsync(u => u.Id == user && u.ClinicId == clinic && u.IsActive && u.Role == Role.Patient &&
                u.IsEmailVerified && u.Email != null && u.Email.Trim() != "")) return Error(403, "Баталгаажсан цахим шуудан шаардлагатай.");
            var patient = await Patients(clinic, user).Select(p => (int?)p.Id).SingleOrDefaultAsync();
            if (patient is null) return Error(404, "Өвчтөний бүртгэл олдсонгүй.");
            validatedPatient = patient;
            var old = await Own(clinic, patient.Value).SingleOrDefaultAsync(a => a.RequestId == request.RequestId);
            if (old is not null) return Replay(old, request, start);
            var decision = await policy.RequireVerifiedAsync(clinic);
            if (decision.TimeZone is null) return Error(409, "Эмнэлгийн цагийн бүсийг баталгаажуулах шаардлагатай.");
            var now = clock.GetUtcNow();
            var local = TimeZoneInfo.ConvertTime(start, decision.TimeZone);
            var date = DateOnly.FromDateTime(local.DateTime);
            if (!InWindow(date, decision.TimeZone, now) || local.Second != 0 || local.Ticks % TimeSpan.TicksPerMinute != 0 || local.Minute % 15 != 0)
                return Error(400, "Цаг нь 15 минутын алхамтай, дараагийн 30 хоногт байх ёстой.");
            var config = await Configuration(clinic, request.DoctorProfileId, request.ServiceId);
            if (config is null) return Error(404, "Эмч эсвэл үйлчилгээ олдсонгүй.");
            var slot = Candidate(date, local.Hour * 60 + local.Minute, decision.TimeZone, config,
                await Hours(clinic, request.DoctorProfileId), now);
            if (slot is null || slot.StartUtc != start || await Overlap(clinic, patient.Value, request.DoctorProfileId, slot, config))
                return Conflict();
            var appointment = new Appointment
            {
                ClinicId = clinic, PatientId = patient.Value, DoctorProfileId = request.DoctorProfileId, ServiceId = request.ServiceId,
                StartUtc = slot.StartUtc, EndUtc = slot.EndUtc,
                OccupiedStartUtc = slot.StartUtc.AddMinutes(-config.BufferBeforeMinutes),
                OccupiedEndUtc = slot.EndUtc.AddMinutes(config.BufferAfterMinutes),
                DurationMinutesSnapshot = config.DurationMinutes, BufferBeforeMinutesSnapshot = config.BufferBeforeMinutes,
                BufferAfterMinutesSnapshot = config.BufferAfterMinutes, ClinicTimeZoneIdSnapshot = decision.TimeZone.Id,
                Status = AppointmentStatus.Confirmed, RequestId = request.RequestId, CreatedAtUtc = clock.GetUtcNow()
            };
            db.Appointments.Add(appointment);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return new(201, Response(appointment));
        }
        catch (Exception e) when (DatabaseCompatibility.IsConflict(e, sqliteUnique: true))
        {
            // No retry of a booking transaction. One fresh read can recognize a committed duplicate.
            db.ChangeTracker.Clear();
            if (!validatedPatient.HasValue) return Conflict();
            try
            {
                var patient = await Patients(clinic, user).Select(p => (int?)p.Id).SingleOrDefaultAsync();
                if (patient.HasValue)
                {
                    var old = await Own(clinic, patient.Value).SingleOrDefaultAsync(a => a.RequestId == request.RequestId);
                    if (old is not null) return Replay(old, request, start);
                }
            }
            catch (Exception lookup) when (DatabaseCompatibility.IsConflict(lookup)) { }
            return Conflict();
        }
    }

    public async Task<AppointmentResult> Read(int clinic, int user, int? id, int page = 1, int pageSize = 20)
    {
        if (id <= 0 || page < 1 || page > 100000 || pageSize is < 1 or > 100) return Error(400, "Дугаар эсвэл хуудасны хэмжээ буруу байна.");
        var patient = await Patients(clinic, user).Select(p => (int?)p.Id).SingleOrDefaultAsync();
        if (patient is null) return Error(404, "Өвчтөний бүртгэл олдсонгүй.");
        var query = Own(clinic, patient.Value);
        if (id.HasValue)
        {
            var row = await query.SingleOrDefaultAsync(a => a.Id == id);
            return row is null ? Error(404, "Захиалга олдсонгүй.") : new(200, Response(row));
        }
        var rows = await query.OrderByDescending(a => a.StartUtc).ThenByDescending(a => a.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync();
        return new(200, new AppointmentPage(page, pageSize, rows.Select(Response).ToArray()));
    }
}
