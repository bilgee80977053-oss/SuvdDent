using System.Globalization;
using DentalApi.Data;
using DentalApi.Models;
using Microsoft.EntityFrameworkCore;

namespace DentalApi.Services;

public interface IAppointmentConfirmationNotifications
{
    void Record(Appointment appointment, DateTime createdAtUtc);
}

public sealed class AppointmentConfirmationNotifications(AppDbContext db) : IAppointmentConfirmationNotifications
{
    public void Record(Appointment appointment, DateTime createdAtUtc)
    {
        var local = TimeZoneInfo.ConvertTime(appointment.StartUtc,
            TimeZoneInfo.FindSystemTimeZoneById(appointment.TimeZoneIdSnapshot));
        var time = local.ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture);
        var reference = $"NX-{appointment.Id}";
        foreach (var channel in new[] { "Email", "Sms" })
        {
            db.AppointmentNotificationOutbox.Add(new AppointmentNotification
            {
                AppointmentId = appointment.Id, ClinicId = appointment.ClinicId,
                PlatformAccountId = appointment.PlatformAccountId, Channel = channel, CreatedAtUtc = createdAtUtc,
                Subject = channel == "Email" ? $"[DEMO] Appointment {reference}" : null,
                Body = channel == "Email"
                    ? $"DEMO ONLY - no email sent.\nReference: {reference}\nDoctor: {appointment.DoctorNameSnapshot}\nService: {appointment.ServiceNameSnapshot}\nTime: {time}\nClinic timezone: {appointment.TimeZoneIdSnapshot}"
                    : $"DEMO - no SMS sent. {reference}: {time} ({appointment.TimeZoneIdSnapshot})."
            });
        }
    }
}

public sealed record DemoConfirmation(Guid NotificationId, string Channel, string? Subject, string Body);

// Deliberately has no recipient/contact field and no SMTP/SMS transport dependency.
public interface IDemoAppointmentNotificationProvider
{
    Task<string> GenerateAsync(DemoConfirmation confirmation, CancellationToken cancellationToken);
}

public sealed class DemoAppointmentNotificationProvider : IDemoAppointmentNotificationProvider
{
    public Task<string> GenerateAsync(DemoConfirmation confirmation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (confirmation.Channel is not ("Email" or "Sms") || !confirmation.Body.StartsWith("DEMO", StringComparison.Ordinal))
            throw new InvalidOperationException("Invalid DEMO confirmation.");
        return Task.FromResult("DemoGenerated");
    }
}

// Internal processing boundary: not exposed by a controller or public query endpoint.
public sealed class AppointmentNotificationProcessor(AppDbContext db,
    IDemoAppointmentNotificationProvider provider, TimeProvider clock)
{
    public static readonly TimeSpan ContentRetention = TimeSpan.FromHours(24);

    public async Task<int> CleanupAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = clock.GetUtcNow().UtcDateTime.Subtract(ContentRetention);
        // Keep identity/channel tombstones for deduplication; erase content even on pending/failed rows.
        return await db.AppointmentNotificationOutbox.Where(x => x.CreatedAtUtc <= cutoff &&
            (x.Body != null || x.Subject != null || x.Status == "Pending" || x.Status == "Processing"))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Body, (string?)null).SetProperty(x => x.Subject, (string?)null)
                .SetProperty(x => x.Status, x => x.Status == "DemoGenerated" ? "DemoGenerated" : "Failed")
                .SetProperty(x => x.LastErrorCode, "DEMO_CONTENT_EXPIRED")
                .SetProperty(x => x.LeaseToken, (Guid?)null).SetProperty(x => x.LeaseExpiresAtUtc, (DateTime?)null)
                .SetProperty(x => x.NextAttemptAtUtc, (DateTime?)null), cancellationToken);
    }

    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken = default)
    {
        await CleanupAsync(cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        // A process that crashed on its final lease must not leave an immortal Processing row.
        await db.AppointmentNotificationOutbox.Where(x => x.Status == "Processing" &&
            x.LeaseExpiresAtUtc <= now && x.AttemptCount >= 3)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "Failed")
                .SetProperty(x => x.LastErrorCode, "DEMO_ATTEMPTS_EXHAUSTED")
                .SetProperty(x => x.LeaseToken, (Guid?)null).SetProperty(x => x.LeaseExpiresAtUtc, (DateTime?)null), cancellationToken);
        var ids = await Eligible(now).OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .Select(x => x.Id).Take(20).ToArrayAsync(cancellationToken);
        var processed = 0;
        foreach (var id in ids)
        {
            var lease = Guid.NewGuid();
            var claimed = await Eligible(now).Where(x => x.Id == id).ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, "Processing").SetProperty(x => x.LeaseToken, (Guid?)lease)
                .SetProperty(x => x.LeaseExpiresAtUtc, (DateTime?)now.AddMinutes(2))
                .SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1), cancellationToken);
            if (claimed == 0) continue;
            var row = await db.AppointmentNotificationOutbox.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == id && x.LeaseToken == lease, cancellationToken);
            if (row is null || row.Body is null) continue;
            try
            {
                // No booking transaction is active here; provider failure cannot undo an appointment.
                var result = await provider.GenerateAsync(new(row.Id, row.Channel, row.Subject, row.Body), cancellationToken);
                if (result != "DemoGenerated") throw new InvalidOperationException("Invalid DEMO result.");
                processed += await db.AppointmentNotificationOutbox.Where(x => x.Id == id && x.LeaseToken == lease)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "DemoGenerated")
                        .SetProperty(x => x.ProcessedAtUtc, (DateTime?)clock.GetUtcNow().UtcDateTime)
                        .SetProperty(x => x.LastErrorCode, (string?)null).SetProperty(x => x.NextAttemptAtUtc, (DateTime?)null)
                        .SetProperty(x => x.LeaseToken, (Guid?)null).SetProperty(x => x.LeaseExpiresAtUtc, (DateTime?)null), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception)
            {
                // Never persist/log provider exception text, contact information or message content.
                await db.AppointmentNotificationOutbox.Where(x => x.Id == id && x.LeaseToken == lease)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, row.AttemptCount >= 3 ? "Failed" : "Pending")
                        .SetProperty(x => x.LastErrorCode, "DEMO_PROVIDER_FAILURE")
                        .SetProperty(x => x.NextAttemptAtUtc, (DateTime?)now.AddMinutes(1))
                        .SetProperty(x => x.LeaseToken, (Guid?)null).SetProperty(x => x.LeaseExpiresAtUtc, (DateTime?)null), cancellationToken);
            }
        }
        return processed;
    }

    private IQueryable<AppointmentNotification> Eligible(DateTime now) => db.AppointmentNotificationOutbox.Where(x =>
        x.Mode == "Demo" && x.Body != null && x.AttemptCount < 3 &&
        ((x.Status == "Pending" && (x.NextAttemptAtUtc == null || x.NextAttemptAtUtc <= now)) ||
         (x.Status == "Processing" && x.LeaseExpiresAtUtc <= now)));
}

public sealed class DemoNotificationWorker(IServiceScopeFactory scopes) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<AppointmentNotificationProcessor>().ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { /* No exception/content logging. Durable rows remain for the next tick. */ }
        }
    }
}
