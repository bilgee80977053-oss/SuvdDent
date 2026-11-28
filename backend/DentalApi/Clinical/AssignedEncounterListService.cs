using System.Data;
using System.Globalization;
using System.Security.Claims;
using DentalApi.Data;
using DentalApi.Models;
using DentalApi.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DentalApi.Clinical;

internal sealed record AssignedEncounterItem(int AppointmentId, DateTimeOffset StartUtc, DateTimeOffset EndUtc,
    int ServiceId, string ServiceLabel);
internal sealed record AssignedEncounterPage(int Page, int PageSize, IReadOnlyList<AssignedEncounterItem> Items);
internal sealed record AssignedEncounterResult(PerformedProcedureReadStatus Status, AssignedEncounterPage? Page = null);

internal sealed class AssignedEncounterListService(AppDbContext db, IWebHostEnvironment environment,
    ClinicalEncounterAccess encounters, ClinicalAuditTrail audit)
{
    internal async Task<AssignedEncounterResult> ListAsync(ClaimsPrincipal actor, string? from, string? until,
        int page, int pageSize, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!PerformedProcedureService.IsTestingMemorySqlite(environment, db) ||
            !ClinicalEncounterAccess.TryReadDoctorIdentity(actor, out var clinicId, out var userId))
            return new(PerformedProcedureReadStatus.Unavailable);
        if (page is < 1 or > 100 || pageSize is < 1 or > 20 ||
            !DateOnly.TryParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) ||
            !DateOnly.TryParseExact(until, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end) ||
            end.DayNumber - start.DayNumber is < 1 or > 31)
            return new(PerformedProcedureReadStatus.Invalid);
        if (db.Database.CurrentTransaction is not null || db.ChangeTracker.HasChanges())
            return new(PerformedProcedureReadStatus.StorageUnavailable);

        var fromUtc = new DateTimeOffset(start.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var untilUtc = new DateTimeOffset(end.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var staged = new List<ClinicalAuditEvent>();
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            var rows = await (
                from encounter in encounters.AssignedEncounters(clinicId, userId)
                join service in db.Services.AsNoTracking()
                    on new { encounter.ClinicId, encounter.ServiceId }
                    equals new { service.ClinicId, ServiceId = service.Id }
                where encounter.StartUtc >= fromUtc && encounter.StartUtc < untilUtc && service.IsActive &&
                    db.DoctorServices.Any(link => link.ClinicId == encounter.ClinicId &&
                        link.DoctorProfileId == encounter.DoctorProfileId && link.ServiceId == encounter.ServiceId && link.IsActive)
                orderby encounter.StartUtc descending, encounter.Id descending
                select new { encounter.PatientId, Item = new AssignedEncounterItem(
                    encounter.Id, encounter.StartUtc, encounter.EndUtc, service.Id, service.Name) })
                .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(token);
            if (rows.Any(row => string.IsNullOrWhiteSpace(row.Item.ServiceLabel) ||
                row.Item.ServiceLabel.Length is < 2 or > 160 || row.Item.ServiceLabel.Any(char.IsControl)))
                return new(PerformedProcedureReadStatus.StorageUnavailable);

            // Use existing appointment-scoped audit. Return no total, look-ahead,
            // or ids outside this audited page. Empty pages release no encounter data.
            var correlation = Guid.NewGuid();
            foreach (var row in rows)
                staged.Add(audit.Stage(new(clinicId, row.PatientId, row.Item.AppointmentId, userId),
                    ClinicalAuditAction.HistoryRead, correlation));

            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            return new(PerformedProcedureReadStatus.Read, new(page, pageSize, rows.Select(row => row.Item).ToArray()));
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
