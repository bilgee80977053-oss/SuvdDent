using System.Data;
using System.Text.RegularExpressions;
using DentalApi.Contracts;
using DentalApi.Data;
using DentalApi.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DentalApi.Services;

public sealed record PhoneVerificationResult(int Status, object Data);

public sealed class PhoneVerificationService(AppDbContext db, VerificationService codes,
    IPhoneVerificationSmsSender sms, TimeProvider clock)
{
    private const string InvalidCode = "Код буруу эсвэл ашиглах боломжгүй байна.";
    private static PhoneVerificationResult Error(int status, string message) => new(status, new { message });
    private static string? Destination(string? phone)
    {
        if (phone is null || phone.Length > 32) return null;
        var value = phone.Trim();
        if (Regex.IsMatch(value, @"\A[0-9]{8}\z")) value = "+976" + value;
        else if (Regex.IsMatch(value, @"\A976[0-9]{8}\z")) value = "+" + value;
        return Regex.IsMatch(value, @"\A[+]976[0-9]{8}\z") ? value : null;
    }
    private static string Mask(string destination) => "+976****" + destination[^4..];
    private PhoneVerificationStatusResponse Response(User user)
    {
        var destination = Destination(user.Phone);
        return new(destination is null ? null : Mask(destination), user.IsPhoneVerified,
            destination is not null, sms.IsAvailable);
    }
    private Task<IDbContextTransaction> Begin(CancellationToken ct) =>
        db.Database.IsNpgsql()
            ? db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct)
            : db.Database.BeginTransactionAsync(ct);

    // Every mutating operation locks this same user before reading quota/challenges.
    // SQLite's non-deferred write transaction provides equivalent exclusion.
    private async Task<User?> LockedUser(int clinic, int user, CancellationToken ct) =>
        db.Database.IsNpgsql()
            ? (await db.Users.FromSqlInterpolated($"""
                SELECT * FROM "Users" WHERE "ClinicId" = {clinic} AND "Id" = {user} FOR UPDATE
                """).ToListAsync(ct)).SingleOrDefault(x => x.IsActive && x.Role == Role.Patient)
            : await db.Users.SingleOrDefaultAsync(x => x.ClinicId == clinic && x.Id == user &&
                x.IsActive && x.Role == Role.Patient, ct);

    private IQueryable<PhoneVerificationChallenge> Own(int clinic, int user) =>
        db.Set<PhoneVerificationChallenge>().Where(x => x.ClinicId == clinic && x.UserId == user);

    // Conservative rolling budget: all failures on a recently failed challenge
    // count, including failures on invalidated/consumed challenges after resend.
    private async Task<int> Failures(int clinic, int user, long now, CancellationToken ct) =>
        await Own(clinic, user).Where(x => x.LastFailedUtcTicks != null &&
            x.LastFailedUtcTicks > now - TimeSpan.FromMinutes(15).Ticks)
            .SumAsync(x => (int?)x.FailedAttempts, ct) ?? 0;

    public async Task<PhoneVerificationResult> Status(int clinic, int user, CancellationToken ct)
    {
        var account = await db.Users.AsNoTracking().SingleOrDefaultAsync(x =>
            x.ClinicId == clinic && x.Id == user && x.IsActive && x.Role == Role.Patient, ct);
        return account is null ? Error(404, "Бүртгэл олдсонгүй.") : new(200, Response(account));
    }

    public async Task<PhoneVerificationResult> Request(int clinic, int user, CancellationToken ct)
    {
        if (!sms.IsAvailable) return Error(503, "SMS баталгаажуулалт одоогоор боломжгүй байна.");
        PhoneVerificationChallenge challenge;
        string code;
        try
        {
            db.ChangeTracker.Clear();
            await using var tx = await Begin(ct);
            var account = await LockedUser(clinic, user, ct);
            if (account is null) return Error(404, "Бүртгэл олдсонгүй.");
            var destination = Destination(account.Phone);
            if (destination is null) return Error(400, "Бүртгэлийн утасны дугаар баталгаажуулах боломжгүй байна.");
            if (account.IsPhoneVerified) return Error(409, "Утас аль хэдийн баталгаажсан байна.");
            var now = clock.GetUtcNow().UtcTicks;
            var latest = await Own(clinic, user).MaxAsync(x => (long?)x.CreatedUtcTicks, ct);
            if (latest.HasValue && latest.Value + TimeSpan.FromSeconds(60).Ticks > now)
                return Error(429, "Шинэ код хүсэхээс өмнө түр хүлээнэ үү.");
            if (await Own(clinic, user).CountAsync(x => x.CreatedUtcTicks > now - TimeSpan.FromHours(24).Ticks, ct) >= 5 ||
                await Failures(clinic, user, now, ct) >= 5)
                return Error(429, "Баталгаажуулалтын оролдлогын хязгаар хүрсэн.");

            await Own(clinic, user).Where(x => !x.IsConsumed && !x.IsInvalidated)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsInvalidated, true), ct);
            code = codes.GenerateCode();
            challenge = new PhoneVerificationChallenge
            {
                ClinicId = clinic, UserId = user, PhoneSnapshot = account.Phone!,
                Destination = destination, CodeHash = codes.HashCode(code), CreatedUtcTicks = now,
                ExpiresUtcTicks = now + TimeSpan.FromSeconds(120).Ticks
            };
            db.Add(challenge);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (Exception error) when (DatabaseCompatibility.IsConflict(error, sqliteUnique: true, sqlitePrimaryKey: true))
        {
            db.ChangeTracker.Clear();
            return Error(409, "Давхардсан хүсэлт байна. Төлөвөө шалгана уу.");
        }

        try
        {
            // Never send before the quota reservation commits; never persist this body.
            await sms.SendAsync(challenge.Destination, code, challenge.Id.ToString("N"), ct);
            if (await RecordDelivery(clinic, user, challenge.Id, true, ct))
                return new(202, new PhoneVerificationRequestedResponse(challenge.Id, Mask(challenge.Destination),
                    new DateTimeOffset(challenge.ExpiresUtcTicks, TimeSpan.Zero),
                    new DateTimeOffset(challenge.CreatedUtcTicks, TimeSpan.Zero).AddSeconds(60)));
        }
        catch (Exception)
        {
            // No provider details, OTPs or credentials are logged or returned.
        }

        // Use an independent short cleanup deadline even when the HTTP request was cancelled.
        // If cleanup cannot commit, Pending remains non-confirmable.
        try
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await RecordDelivery(clinic, user, challenge.Id, false, cleanup.Token);
        }
        catch (Exception) { }
        return Error(503, "Код илгээхийг баталгаажуулж чадсангүй. Дараа дахин хүснэ үү.");
    }

    private async Task<bool> RecordDelivery(int clinic, int user, Guid id, bool accepted, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        await using var tx = await Begin(ct);
        var account = await LockedUser(clinic, user, ct);
        var challenge = await Own(clinic, user).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (challenge is null) return false;
        var eligible = accepted && account is not null && !account.IsPhoneVerified &&
            account.Phone == challenge.PhoneSnapshot && Destination(account.Phone) == challenge.Destination &&
            !challenge.IsConsumed && !challenge.IsInvalidated &&
            challenge.ExpiresUtcTicks > clock.GetUtcNow().UtcTicks;
        challenge.DeliveryState = eligible ? PhoneVerificationDeliveryState.Accepted : PhoneVerificationDeliveryState.Failed;
        if (!eligible) challenge.IsInvalidated = true;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return eligible;
    }

    public async Task<PhoneVerificationResult> Confirm(int clinic, int user, Guid id, string code, CancellationToken ct)
    {
        // Also validate direct service calls; never run expensive hashing on arbitrary input.
        if (!Regex.IsMatch(code, @"\A[0-9]{6}\z")) return Error(400, InvalidCode);
        try
        {
            db.ChangeTracker.Clear();
            await using var tx = await Begin(ct);
            var account = await LockedUser(clinic, user, ct);
            var challenge = await Own(clinic, user).SingleOrDefaultAsync(x => x.Id == id, ct);
            var now = clock.GetUtcNow().UtcTicks;
            if (account is null || challenge is null || challenge.IsConsumed || challenge.IsInvalidated ||
                challenge.DeliveryState != PhoneVerificationDeliveryState.Accepted)
                return Error(400, InvalidCode);
            if (account.Phone != challenge.PhoneSnapshot || Destination(account.Phone) != challenge.Destination ||
                challenge.ExpiresUtcTicks <= now || challenge.FailedAttempts >= 5 || account.IsPhoneVerified)
            {
                challenge.IsInvalidated = true;
                await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
                return Error(400, InvalidCode);
            }
            if (await Failures(clinic, user, now, ct) >= 5)
                return Error(429, "Баталгаажуулалтын оролдлогын хязгаар хүрсэн.");
            if (!codes.VerifyCode(code, challenge.CodeHash))
            {
                challenge.FailedAttempts++;
                challenge.LastFailedUtcTicks = now;
                if (challenge.FailedAttempts >= 5) challenge.IsInvalidated = true;
                await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
                return Error(400, InvalidCode);
            }
            challenge.IsConsumed = true;
            account.IsPhoneVerified = true;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new(200, Response(account));
        }
        catch (Exception error) when (DatabaseCompatibility.IsConflict(error, sqliteUnique: true))
        {
            db.ChangeTracker.Clear();
            return Error(409, "Давхардсан хүсэлт байна. Төлөвөө шалгана уу.");
        }
    }
}
