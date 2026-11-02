using System.Data;
using DentalApi.Data;
using DentalApi.Models;
using Microsoft.EntityFrameworkCore;

namespace DentalApi.Services;

public sealed class NotificationProcessor(AppDbContext db, TimeProvider clock,
    INotificationEmailSender email, ISmsSender sms)
{
    // Keep the row/appointment locks until acceptance is recorded. A second worker
    // cannot deliver the same row concurrently; cancellation uses the same appointment lock.
    // A crash after provider acceptance can replay: delivery keys are stable across retries.
    public async Task<bool> ProcessOneAsync(CancellationToken cancellationToken = default)
    {
        db.ChangeTracker.Clear();
        // Exact row locks provide exclusion here; ReadCommitted avoids replaying
        // an accepted external send due to an unrelated serializable predicate conflict.
        await using var tx = db.Database.IsNpgsql()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            : await DatabaseCompatibility.BeginWriteAsync(db);
        var now = clock.GetUtcNow().UtcTicks;
        NotificationOutbox? row;
        if (db.Database.IsNpgsql())
            row = (await db.Set<NotificationOutbox>().FromSqlInterpolated($"""
                SELECT * FROM "NotificationOutbox"
                WHERE "State" = 'Pending' AND "NextAttemptUtcTicks" <= {now}
                ORDER BY "NextAttemptUtcTicks", "Id" LIMIT 1 FOR UPDATE SKIP LOCKED
                """).ToListAsync(cancellationToken)).SingleOrDefault();
        else
            row = await db.Set<NotificationOutbox>().Where(x => x.State == NotificationState.Pending &&
                x.NextAttemptUtcTicks <= now).OrderBy(x => x.NextAttemptUtcTicks).ThenBy(x => x.Id)
                .FirstOrDefaultAsync(cancellationToken);
        if (row is null) return false;
        var appointments = db.Appointments.Where(x => x.ClinicId == row.ClinicId && x.Id == row.AppointmentId);
        var appointment = db.Database.IsNpgsql()
            ? (await db.Appointments.FromSqlInterpolated($"""
                SELECT * FROM "Appointments" WHERE "ClinicId" = {row.ClinicId} AND "Id" = {row.AppointmentId} FOR UPDATE
                """).ToListAsync(cancellationToken)).Single()
            : await appointments.SingleAsync(cancellationToken);
        var user = await (from p in db.Patients join u in db.Users
                          on new { p.ClinicId, Id = p.UserId } equals new { u.ClinicId, Id = (int?)u.Id }
                          where p.ClinicId == row.ClinicId && p.Id == appointment.PatientId && p.IsActive && u.IsActive && u.Role == Role.Patient
                          select u).SingleOrDefaultAsync(cancellationToken);
        var consent = user is null ? null : await db.Set<NotificationPreference>()
            .SingleOrDefaultAsync(x => x.ClinicId == row.ClinicId && x.UserId == user.Id, cancellationToken);
        var eligible = user is not null && consent is not null &&
            (row.Channel == NotificationChannel.Email
                ? consent.EmailEnabled && user.IsEmailVerified && user.Email == row.Recipient
                : consent.SmsEnabled && user.IsPhoneVerified && user.Phone == row.Recipient);
        var correctStatus = row.Event == NotificationEvent.Cancelled
            ? appointment.Status == AppointmentStatus.Cancelled
            : appointment.Status == AppointmentStatus.Confirmed;
        if (!eligible || !correctStatus || (row.Event == NotificationEvent.Reminder && appointment.StartUtc.UtcTicks <= now))
            row.State = NotificationState.Suppressed;
        else
        {
            row.Attempts++;
            try
            {
                var key = row.Id.ToString("N");
                if (row.Channel == NotificationChannel.Email)
                    await email.SendAsync(row.Recipient, row.Body, key, cancellationToken);
                else
                    await sms.SendAsync(row.Recipient, row.Body, key, cancellationToken);
                row.State = NotificationState.Sent;
                row.SentUtcTicks = clock.GetUtcNow().UtcTicks;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception)
            {
                // Never store provider messages: they may contain credentials or contact details.
                row.State = row.Attempts >= 8 ? NotificationState.Failed : NotificationState.Pending;
                row.NextAttemptUtcTicks = clock.GetUtcNow().AddMinutes(Math.Min(60, Math.Pow(2, row.Attempts - 1))).UtcTicks;
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return true;
    }
}

public sealed class NotificationWorker(IServiceScopeFactory scopes, IConfiguration config,
    IHostEnvironment environment, ILogger<NotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Tests drive processors explicitly with fake transports; never start real delivery.
        if (environment.IsEnvironment("Testing") || !config.GetValue<bool>("Notifications:WorkerEnabled")) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                if (await scope.ServiceProvider.GetRequiredService<NotificationProcessor>().ProcessOneAsync(stoppingToken))
                    continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                logger.LogWarning("Notification processing failed ({FailureType}); polling will retry.", error.GetType().Name);
            }
            try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
