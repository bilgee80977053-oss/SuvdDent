using System.Data;
using System.Security.Claims;
using DentalApi.Data;
using DentalApi.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DentalApi.Services;

public enum EncounterTreatmentOutcome { Recorded, Read, Missing, Invalid, Duplicate, StorageUnavailable }
public sealed record EncounterTreatmentNote(int AppointmentId, string Note, DateTimeOffset SignedUtc);
public sealed record EncounterTreatmentResult(EncounterTreatmentOutcome Outcome, EncounterTreatmentNote? Record = null);

public interface IEncounterTreatmentService
{
    Task<EncounterTreatmentOutcome> SignAsync(ClaimsPrincipal actor, int appointmentId, string? note, CancellationToken token);
    Task<EncounterTreatmentResult> ReadAsync(ClaimsPrincipal actor, int appointmentId, CancellationToken token);
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
