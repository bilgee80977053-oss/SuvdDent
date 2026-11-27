using System.Data;
using System.Security.Claims;
using DentalApi.Controllers;
using DentalApi.Data;
using DentalApi.Models;
using DentalApi.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DentalApi.Clinical;

internal enum PerformedProcedureStatus { Invalid, Unavailable, Conflict, Recorded, Replayed, StorageUnavailable }
internal sealed record PerformedProcedureReceipt(
    int AppointmentId, Guid RequestId, int ServiceId, string ServiceLabel, StructuredTreatmentTarget Target, DateTimeOffset SignedUtc);
internal sealed record PerformedProcedureResult(PerformedProcedureStatus Status, PerformedProcedureReceipt? Receipt = null);

internal enum PerformedProcedureReadStatus { Invalid, Unavailable, Read, StorageUnavailable }
internal sealed record PerformedProcedureListItem(
    Guid RequestId, int ServiceId, string ServiceLabel, string Scope, string? ToothCode, DateTimeOffset SignedUtc);
internal sealed record PerformedProcedurePage(
    int Page, int PageSize, bool HasMore, IReadOnlyList<PerformedProcedureListItem> Items);
internal sealed record PerformedProcedureReadResult(PerformedProcedureReadStatus Status, PerformedProcedurePage? Page = null);

internal sealed class PerformedProcedureService(
    AppDbContext db, IWebHostEnvironment environment, StructuredProcedureDraftService drafts,
    ClinicalEncounterAccess encounters, ClinicalAuditTrail audit, TimeProvider clock)
{
    internal static bool IsTestingMemorySqlite(IWebHostEnvironment environment, AppDbContext db)
        => environment.IsEnvironment("Testing") && db.Database.IsSqlite() &&
           EncounterTreatmentController.IsMemorySqliteConnection(db.Database.GetConnectionString());

    internal async Task<PerformedProcedureReadResult> ListAsync(ClaimsPrincipal actor, int appointmentId,
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        const int maxPage = 1000;
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsTestingMemorySqlite(environment, db))
            return new(PerformedProcedureReadStatus.Unavailable);
        if (appointmentId <= 0 || page is < 1 or > maxPage || pageSize is < 1 or > 50)
            return new(PerformedProcedureReadStatus.Invalid);
        if (db.Database.CurrentTransaction is not null || db.ChangeTracker.HasChanges())
            return new(PerformedProcedureReadStatus.StorageUnavailable);

        ClinicalAuditEvent? auditEvent = null;
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var encounter = await encounters.FindAsync(actor, appointmentId, cancellationToken);
            if (encounter is null)
                return new(PerformedProcedureReadStatus.Unavailable);

            // This is signing history: do not join a mutable service label to the signed rows.
            var items = await db.Set<PerformedProcedure>().AsNoTracking()
                .Where(row => row.ClinicId == encounter.ClinicId && row.AppointmentId == encounter.AppointmentId &&
                              row.DoctorUserId == encounter.DoctorUserId)
                .OrderBy(row => row.SignedUtc).ThenBy(row => row.RequestId)
                .Skip((page - 1) * pageSize).Take(pageSize + 1)
                .Select(row => new PerformedProcedureListItem(row.RequestId, row.ServiceId, row.ServiceLabel,
                    row.TargetScope, row.ToothCode, row.SignedUtc))
                .ToListAsync(cancellationToken);
            var overflow = items.Count > pageSize;
            if (overflow)
                items.RemoveAt(pageSize);

            auditEvent = audit.Stage(encounter, ClinicalAuditAction.HistoryRead, Guid.NewGuid());
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(PerformedProcedureReadStatus.Read, new(page, pageSize, overflow && page < maxPage, items));
        }
        catch (Exception error) when (error is DbUpdateException or SqliteException)
        {
            return new(PerformedProcedureReadStatus.StorageUnavailable);
        }
        finally
        {
            if (auditEvent is not null) db.Entry(auditEvent).State = EntityState.Detached;
        }
    }

    // This is an internal synthetic test seam, not an enabled workflow or an authorization token.
    internal async Task<PerformedProcedureResult> SignAsync(ClaimsPrincipal actor,
        StructuredProcedureDraftInput input, Guid requestId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsTestingMemorySqlite(environment, db))
            return new(PerformedProcedureStatus.Unavailable);
        if (requestId == Guid.Empty)
            return new(PerformedProcedureStatus.Invalid);
        // Never commit an unrelated caller's pending changes as part of this signed write.
        if (db.Database.CurrentTransaction is not null || db.ChangeTracker.HasChanges())
            return new(PerformedProcedureStatus.StorageUnavailable);

        PerformedProcedure? item = null;
        ClinicalAuditEvent? auditEvent = null;
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var draft = await drafts.ValidateAsync(actor, input, cancellationToken);
            if (draft.Status == StructuredProcedureDraftStatus.Invalid)
                return new(PerformedProcedureStatus.Invalid);
            if (draft.Snapshot is not { } resolved)
                return new(PerformedProcedureStatus.Unavailable);

            var qualified = await db.Appointments.AsNoTracking().AnyAsync(visit =>
                visit.ClinicId == resolved.ClinicId && visit.Id == resolved.AppointmentId &&
                visit.ServiceId == resolved.ServiceId &&
                db.DoctorServices.Any(assignment => assignment.ClinicId == visit.ClinicId &&
                    assignment.DoctorProfileId == visit.DoctorProfileId &&
                    assignment.ServiceId == visit.ServiceId && assignment.IsActive),
                cancellationToken);
            if (!qualified)
                return new(PerformedProcedureStatus.Unavailable);

            var existing = await db.Set<PerformedProcedure>().AsNoTracking().SingleOrDefaultAsync(row =>
                row.ClinicId == resolved.ClinicId && row.AppointmentId == resolved.AppointmentId &&
                row.RequestId == requestId, cancellationToken);
            var targetScope = resolved.Target.Scope.ToString();
            var toothCode = resolved.Target.Tooth?.Code;
            if (existing is not null)
                return existing.DoctorUserId == resolved.DoctorUserId &&
                       existing.ServiceId == resolved.ServiceId && existing.TargetScope == targetScope && existing.ToothCode == toothCode
                    ? new(PerformedProcedureStatus.Replayed) // No record read/audit bypass via an exact replay.
                    : new(PerformedProcedureStatus.Conflict);

            item = new PerformedProcedure
            {
                ClinicId = resolved.ClinicId, AppointmentId = resolved.AppointmentId, RequestId = requestId,
                DoctorUserId = resolved.DoctorUserId, ServiceId = resolved.ServiceId, ServiceLabel = resolved.ServiceLabel,
                TargetScope = targetScope, ToothCode = toothCode, SignedUtc = clock.GetUtcNow()
            };
            db.Add(item);
            auditEvent = audit.Stage(new(resolved.ClinicId, resolved.PatientId, resolved.AppointmentId, resolved.DoctorUserId),
                ClinicalAuditAction.TreatmentRecorded, Guid.NewGuid());
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(PerformedProcedureStatus.Recorded, new(item.AppointmentId, item.RequestId,
                item.ServiceId, item.ServiceLabel, resolved.Target, item.SignedUtc));
        }
        catch (Exception error) when (error is DbUpdateException or SqliteException)
        {
            return new(PerformedProcedureStatus.StorageUnavailable);
        }
        finally
        {
            // The context may survive a failed commit; do not leave these rows tracked for a later save.
            if (item is not null) db.Entry(item).State = EntityState.Detached;
            if (auditEvent is not null) db.Entry(auditEvent).State = EntityState.Detached;
        }
    }
}
