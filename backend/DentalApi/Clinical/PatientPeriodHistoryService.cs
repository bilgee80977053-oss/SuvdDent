using System.Data;
using System.Globalization;
using System.Security.Claims;
using DentalApi.Data;
using DentalApi.Models;
using DentalApi.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DentalApi.Clinical;

internal sealed record PatientHistoryVisit(int AppointmentId, DateTimeOffset StartedUtc);
internal sealed record PatientHistoryPage(int Page, int PageSize, bool HasMore, IReadOnlyList<PatientHistoryVisit> Items);
internal sealed record PatientHistoryResult(PerformedProcedureReadStatus Status, PatientHistoryPage? Page = null);

internal sealed class PatientPeriodHistoryService(
    AppDbContext db, IWebHostEnvironment environment, ClinicalEncounterAccess encounters, ClinicalAuditTrail audit)
{
    internal async Task<PatientHistoryResult> ListAsync(ClaimsPrincipal doctor, int anchorAppointmentId,
        string? from, string? until, int page, int pageSize, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!PerformedProcedureService.IsTestingMemorySqlite(environment, db))
            return new(PerformedProcedureReadStatus.Unavailable);
        if (anchorAppointmentId <= 0 || page is < 1 or > 100 || pageSize is < 1 or > 20 ||
            !DateOnly.TryParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) ||
            !DateOnly.TryParseExact(until, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end) ||
            end.DayNumber - start.DayNumber is < 1 or > 31)
            return new(PerformedProcedureReadStatus.Invalid);
        if (db.Database.CurrentTransaction is not null || db.ChangeTracker.HasChanges())
            return new(PerformedProcedureReadStatus.StorageUnavailable);

        var staged = new List<ClinicalAuditEvent>();
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            var anchor = await encounters.FindAsync(doctor, anchorAppointmentId, token);
            if (anchor is null)
                return new(PerformedProcedureReadStatus.Unavailable);

            var fromUtc = new DateTimeOffset(start.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var untilUtc = new DateTimeOffset(end.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var scoped = encounters.AssignedEncounters(anchor.ClinicId, anchor.DoctorUserId);
            var visits = await (
                from visit in scoped
                where visit.PatientId == anchor.PatientId && visit.StartUtc >= fromUtc && visit.StartUtc < untilUtc &&
                      db.Set<PerformedProcedure>().Any(item => item.ClinicId == visit.ClinicId &&
                          item.AppointmentId == visit.Id && item.DoctorUserId == anchor.DoctorUserId)
                orderby visit.StartUtc descending, visit.Id descending
                select new PatientHistoryVisit(visit.Id, visit.StartUtc))
                .Skip((page - 1) * pageSize).Take(pageSize + 1).ToListAsync(token);
            var overflow = visits.Count > pageSize;
            if (overflow)
                visits.RemoveAt(pageSize);

            // Audit the anchor once, and each distinct visit whose identity is released.
            var correlation = Guid.NewGuid();
            staged.Add(audit.Stage(anchor, ClinicalAuditAction.HistoryRead, correlation));
            foreach (var visit in visits.Where(row => row.AppointmentId != anchor.AppointmentId))
                staged.Add(audit.Stage(anchor with { AppointmentId = visit.AppointmentId }, ClinicalAuditAction.HistoryRead, correlation));

            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            return new(PerformedProcedureReadStatus.Read, new(page, pageSize, overflow && page < 100, visits));
        }
        catch (Exception error) when (error is DbUpdateException or SqliteException)
        {
            return new(PerformedProcedureReadStatus.StorageUnavailable);
        }
        finally
        {
            foreach (var entry in staged)
                db.Entry(entry).State = EntityState.Detached;
        }
    }
}
