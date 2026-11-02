using DentalApi.Contracts;
using DentalApi.Data;
using DentalApi.Models;
using Microsoft.EntityFrameworkCore;

namespace DentalApi.Services;

public sealed class NotificationPreferenceService(AppDbContext db)
{
    public async Task<NotificationPreferencesResponse?> Read(int clinic, int user, CancellationToken ct)
    {
        var contact = await db.Users.AsNoTracking().Where(x =>
            x.ClinicId == clinic && x.Id == user && x.IsActive && x.Role == Role.Patient)
            .Select(x => new { Email = x.IsEmailVerified && x.Email != null && x.Email.Trim() != "",
                Phone = x.IsPhoneVerified && x.Phone != null && x.Phone.Trim() != "" }).SingleOrDefaultAsync(ct);
        if (contact is null) return null;
        var preference = await db.Set<NotificationPreference>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.ClinicId == clinic && x.UserId == user, ct);
        return new(preference?.EmailEnabled ?? false, preference?.SmsEnabled ?? false, contact.Email, contact.Phone);
    }

    public async Task<NotificationPreferencesResponse?> Write(int clinic, int user,
        bool email, bool sms, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(x => x.ClinicId == clinic && x.Id == user && x.IsActive &&
                x.Role == Role.Patient, ct)) return null;
        // One atomic statement on both SQLite and PostgreSQL: no read/insert race,
        // no split flag updates. Concurrent writes use last committed write wins.
        // Parameterized values never include client-provided identity.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "NotificationPreference" ("ClinicId", "UserId", "EmailEnabled", "SmsEnabled")
            VALUES ({clinic}, {user}, {email}, {sms})
            ON CONFLICT ("ClinicId", "UserId") DO UPDATE
            SET "EmailEnabled" = excluded."EmailEnabled", "SmsEnabled" = excluded."SmsEnabled"
            """, ct);
        return await Read(clinic, user, ct);
    }
}
