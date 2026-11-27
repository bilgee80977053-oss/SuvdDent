using System.Data;
using System.Security.Claims;
using DentalApi.Data;
using DentalApi.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DentalApi.Services;

public enum EncounterTreatmentOutcome { Recorded, Read, Missing, Invalid, Duplicate, Replayed, StorageUnavailable }
public sealed record EncounterTreatmentNote(int AppointmentId, string Note, DateTimeOffset SignedUtc);
public sealed record EncounterTreatmentResult(EncounterTreatmentOutcome Outcome, EncounterTreatmentNote? Record = null);
public sealed record EncounterTreatmentAddendum(Guid RequestId, string Note, DateTimeOffset SignedUtc);
public sealed record EncounterAddendumResult(EncounterTreatmentOutcome Outcome, EncounterTreatmentAddendum? Record = null);
public sealed record EncounterAddendumReference(Guid RequestId, DateTimeOffset SignedUtc);
public sealed record EncounterAddendaPage(
    int Page, int PageSize, bool HasMore, IReadOnlyList<EncounterAddendumReference> Items);
public sealed record EncounterAddendaPageResult(EncounterTreatmentOutcome Outcome, EncounterAddendaPage? Record = null);

public interface IEncounterTreatmentService
{
    Task<EncounterTreatmentOutcome> SignAsync(ClaimsPrincipal actor, int appointmentId, string? note, CancellationToken token);
    Task<EncounterTreatmentResult> ReadAsync(ClaimsPrincipal actor, int appointmentId, CancellationToken token);
    Task<EncounterTreatmentOutcome> SignAddendumAsync(
        ClaimsPrincipal actor, int appointmentId, Guid requestId, string? note, CancellationToken token);
    Task<EncounterAddendumResult> ReadAddendumAsync(
        ClaimsPrincipal actor, int appointmentId, Guid requestId, CancellationToken token);
    Task<EncounterAddendaPageResult> ListAddendaAsync(
        ClaimsPrincipal actor, int appointmentId, int page, int pageSize, CancellationToken token);
}

internal sealed class EncounterTreatmentService(
    AppDbContext db, ClinicalEncounterAccess scopeService, ClinicalAuditTrail audit, TimeProvider clock)
    : IEncounterTreatmentService
{
    private const int MaxNoteLength = 2000;

    public async Task<EncounterTreatmentOutcome> SignAsync(
        ClaimsPrincipal actor, int appointmentId, string? note, CancellationToken token)
    {
        var clean = Clean(note);
        if (clean is null)
            return EncounterTreatmentOutcome.Invalid;

        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            var scope = await scopeService.FindAsync(actor, appointmentId, token);
            if (scope is null)
                return EncounterTreatmentOutcome.Missing;
            if (await db.Set<TreatmentRecord>().AsNoTracking().AnyAsync(
                    record => record.ClinicId == scope.ClinicId && record.AppointmentId == scope.AppointmentId, token))
                return EncounterTreatmentOutcome.Duplicate;

            db.Add(new TreatmentRecord
            {
                ClinicId = scope.ClinicId,
                AppointmentId = scope.AppointmentId,
                AuthorUserId = scope.DoctorUserId,
                Note = clean,
                SignedUtc = clock.GetUtcNow()
            });
            audit.Stage(scope, ClinicalAuditAction.TreatmentRecorded, Guid.NewGuid());
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            return EncounterTreatmentOutcome.Recorded;
        }
        catch (Exception error) when (error is DbUpdateException or SqliteException)
        {
            // Neither an uncommitted record nor exception details may reach the caller.
            return EncounterTreatmentOutcome.StorageUnavailable;
        }
    }

    public async Task<EncounterTreatmentResult> ReadAsync(
        ClaimsPrincipal actor, int appointmentId, CancellationToken token)
    {
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            var scope = await scopeService.FindAsync(actor, appointmentId, token);
            if (scope is null)
                return new(EncounterTreatmentOutcome.Missing);
            var row = await db.Set<TreatmentRecord>().AsNoTracking()
                .Where(record => record.ClinicId == scope.ClinicId && record.AppointmentId == scope.AppointmentId &&
                                 record.AuthorUserId == scope.DoctorUserId)
                .Select(record => new EncounterTreatmentNote(record.AppointmentId, record.Note, record.SignedUtc))
                .SingleOrDefaultAsync(token);
            if (row is null)
                return new(EncounterTreatmentOutcome.Missing);

            audit.Stage(scope, ClinicalAuditAction.HistoryRead, Guid.NewGuid());
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            return new(EncounterTreatmentOutcome.Read, row);
        }
        catch (Exception error) when (error is DbUpdateException or SqliteException)
        {
            return new(EncounterTreatmentOutcome.StorageUnavailable);
        }
    }

    public async Task<EncounterTreatmentOutcome> SignAddendumAsync(
        ClaimsPrincipal actor, int appointmentId, Guid requestId, string? note, CancellationToken token)
    {
        var clean = Clean(note);
        if (requestId == Guid.Empty || clean is null)
            return EncounterTreatmentOutcome.Invalid;

        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            var scope = await scopeService.FindAsync(actor, appointmentId, token);
            if (scope is null || !await OwnsOriginal(scope, token))
                return EncounterTreatmentOutcome.Missing;

            var existing = await OwnedAddenda(scope).Where(row => row.RequestId == requestId)
                .Select(row => row.Note).SingleOrDefaultAsync(token);
            if (existing is not null)
                return string.Equals(existing, clean, StringComparison.Ordinal)
                    ? EncounterTreatmentOutcome.Replayed : EncounterTreatmentOutcome.Duplicate;

            db.Add(new TreatmentAddendum
            {
                ClinicId = scope.ClinicId, AppointmentId = scope.AppointmentId,
                AuthorUserId = scope.DoctorUserId, RequestId = requestId,
                Note = clean, SignedUtc = clock.GetUtcNow()
            });
            audit.Stage(scope, ClinicalAuditAction.TreatmentAmended, Guid.NewGuid());
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            return EncounterTreatmentOutcome.Recorded;
        }
        catch (Exception error) when (error is DbUpdateException or SqliteException)
        {
            return EncounterTreatmentOutcome.StorageUnavailable;
        }
    }

    public async Task<EncounterAddendumResult> ReadAddendumAsync(
        ClaimsPrincipal actor, int appointmentId, Guid requestId, CancellationToken token)
    {
        if (requestId == Guid.Empty)
            return new(EncounterTreatmentOutcome.Missing);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            var scope = await scopeService.FindAsync(actor, appointmentId, token);
            if (scope is null || !await OwnsOriginal(scope, token))
                return new(EncounterTreatmentOutcome.Missing);
            var row = await OwnedAddenda(scope).Where(item => item.RequestId == requestId)
                .Select(item => new EncounterTreatmentAddendum(item.RequestId, item.Note, item.SignedUtc))
                .SingleOrDefaultAsync(token);
            if (row is null)
                return new(EncounterTreatmentOutcome.Missing);

            audit.Stage(scope, ClinicalAuditAction.HistoryRead, Guid.NewGuid());
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            return new(EncounterTreatmentOutcome.Read, row);
        }
        catch (Exception error) when (error is DbUpdateException or SqliteException)
        {
            return new(EncounterTreatmentOutcome.StorageUnavailable);
        }
    }

    public async Task<EncounterAddendaPageResult> ListAddendaAsync(
        ClaimsPrincipal actor, int appointmentId, int page, int pageSize, CancellationToken token)
    {
        const int maxPage = 1000;
        if (page is < 1 or > maxPage || pageSize is < 1 or > 50)
            return new(EncounterTreatmentOutcome.Invalid);

        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            var scope = await scopeService.FindAsync(actor, appointmentId, token);
            if (scope is null || !await OwnsOriginal(scope, token))
                return new(EncounterTreatmentOutcome.Missing);

            // Do not select the note text in a discovery query.
            var references = await OwnedAddenda(scope).OrderBy(row => row.SignedUtc).ThenBy(row => row.RequestId)
                .Skip((page - 1) * pageSize).Take(pageSize + 1)
                .Select(row => new EncounterAddendumReference(row.RequestId, row.SignedUtc)).ToListAsync(token);
            var overflow = references.Count > pageSize;
            if (overflow)
                references.RemoveAt(pageSize);
            var hasMore = overflow && page < maxPage;

            audit.Stage(scope, ClinicalAuditAction.HistoryRead, Guid.NewGuid());
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            return new(EncounterTreatmentOutcome.Read, new(page, pageSize, hasMore, references));
        }
        catch (Exception error) when (error is DbUpdateException or SqliteException)
        {
            return new(EncounterTreatmentOutcome.StorageUnavailable);
        }
    }

    private Task<bool> OwnsOriginal(ClinicalEncounterScope scope, CancellationToken token)
        => db.Set<TreatmentRecord>().AsNoTracking().AnyAsync(row => row.ClinicId == scope.ClinicId &&
            row.AppointmentId == scope.AppointmentId && row.AuthorUserId == scope.DoctorUserId, token);

    private IQueryable<TreatmentAddendum> OwnedAddenda(ClinicalEncounterScope scope)
        => db.Set<TreatmentAddendum>().AsNoTracking().Where(row => row.ClinicId == scope.ClinicId &&
            row.AppointmentId == scope.AppointmentId && row.AuthorUserId == scope.DoctorUserId);

    private static string? Clean(string? note)
    {
        if (note is null || note.Length > MaxNoteLength)
            return null;
        var normalized = note.ReplaceLineEndings("\n").Trim();
        return normalized.Length is >= 1 and <= MaxNoteLength &&
               !normalized.Any(c => c != '\n' && char.IsControl(c))
            ? normalized : null;
    }
}
