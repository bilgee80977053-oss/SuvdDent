using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DentalApi.Data;
using DentalApi.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DentalApi.Services;

public sealed record RegistrationOtpResult(int Status, object Body, int? RetryAfter = null);

public sealed class RegistrationOtpService(AppDbContext db, VerificationService codes, EmailService email,
    IPhoneVerificationSmsSender sms, TimeProvider clock, IDataProtectionProvider protection)
{
    private const string Accepted = "reg-choice";
    private const string Delivering = "reg-choice-pending";
    private const int Cooldown = 60;
    private const int Lifetime = 120;
    private const int AttemptLimit = 5;
    private const int DailyLimit = 5;

    private ITimeLimitedDataProtector TokenProtector =>
        protection.CreateProtector("SuvdDent.PatientRegistrationOtp.v1").ToTimeLimitedDataProtector();

    private static string AccountBinding(User account) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(account.PasswordHash)));

    private static string Payload(User account) =>
        FormattableString.Invariant($"{account.ClinicId}:{account.Id}:{AccountBinding(account)}");

    private string CreateToken(User account) => TokenProtector.Protect(Payload(account), TimeSpan.FromHours(24));

    private (int Clinic, string Binding)? ResolveToken(int userId, string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 512) return null;
        try
        {
            var parts = TokenProtector.Unprotect(token).Split(':');
            if (parts.Length == 3 &&
                int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var clinic) &&
                int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var user) &&
                clinic > 0 && user == userId && user > 0 && parts[2].Length == 64)
                return (clinic, parts[2]);
        }
        catch (Exception error) when (error is CryptographicException or FormatException) { }
        return null;
    }

    // Legacy email recovery must not issue a code for a token-bound registration.
    public Task<bool> IsChoiceAccountAsync(int userId) => ForAccount(userId).AnyAsync();

    private IQueryable<VerificationCode> ForAccount(int userId) => db.VerificationCodes
        .Where(x => x.UserId == userId && (x.Purpose == Accepted || x.Purpose == Delivering));

    private static RegistrationOtpResult Error(int status, string reason, string error) => new(status, new { error, reason });

    private static RegistrationOtpResult Unavailable() =>
        Error(503, "delivery_unavailable", "Сонгосон сувгаар код илгээх боломжгүй байна. Өөр аргыг сонгоно уу.");

    private static RegistrationOtpResult InvalidRequest() =>
        Error(400, "invalid_registration", "Баталгаажуулалтын хүсэлт хүчингүй байна.");

    private static RegistrationOtpResult Conflict() =>
        Error(409, "registration_conflict", "Хүсэлт давхардсан байна. Түр хүлээгээд дахин оролдоно уу.");

    private Task<IDbContextTransaction> BeginAsync(CancellationToken ct) => db.Database.IsNpgsql()
        ? db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct)
        : db.Database.BeginTransactionAsync(ct);

    private async Task<User?> LockPendingUserAsync(int clinic, int userId, CancellationToken ct) =>
        db.Database.IsNpgsql()
            ? (await db.Users.FromSqlInterpolated($"""
                SELECT * FROM "Users" WHERE "ClinicId" = {clinic} AND "Id" = {userId} FOR UPDATE
                """).ToListAsync(ct)).SingleOrDefault(x => x.Role == Role.Patient && !x.IsActive)
            : await db.Users.SingleOrDefaultAsync(x => x.ClinicId == clinic && x.Id == userId &&
                x.Role == Role.Patient && !x.IsActive, ct);

    private static string? Destination(User user, string method)
    {
        if (method == "email") return user.Email;
        if (method != "sms" || user.Phone is not { } phone) return null;
        if (phone.Length == 8 && phone.All(char.IsAsciiDigit)) return "+976" + phone;
        if (phone.Length == 11 && phone.StartsWith("976", StringComparison.Ordinal) &&
            phone.All(char.IsAsciiDigit)) return "+" + phone;
        return null;
    }

    private RegistrationOtpResult? ValidateMethod(User user, string method)
    {
        if (method is not ("email" or "sms"))
            return Error(400, "invalid_method", "Утас эсвэл имэйл сонгоно уу.");
        if (string.IsNullOrEmpty(Destination(user, method)))
            return Error(400, "invalid_destination", "Сонгосон аргын утас эсвэл имэйл хаягийг зөв оруулна уу.");
        return (method == "email" ? email.IsAvailable : sms.IsAvailable) ? null : Unavailable();
    }

    private VerificationCode BuildChallenge(User account, string method, string code, DateTime now) =>
        new()
        {
            UserId = account.Id, Channel = method, Purpose = Delivering,
            CodeHash = codes.HashCode(code), CreatedAt = now, ExpiresAt = now.AddSeconds(Lifetime)
        };

    public async Task<RegistrationOtpResult?> RecoverAsync(User candidate, string? phone, string? address,
        string password, string method, CancellationToken ct)
    {
        // Reopening a form or expiry must not permanently strand a pending registration.
        // Require the original password and exact destinations; never let recovery replace contact
        // details, act on an active user, or take control of a legacy registration.
        if (candidate.IsActive || candidate.Role != Role.Patient ||
            candidate.Phone != (string.IsNullOrWhiteSpace(phone) ? null : phone) ||
            candidate.Email != (string.IsNullOrWhiteSpace(address) ? null : address) ||
            !await ForAccount(candidate.Id).AnyAsync(ct) ||
            !BCrypt.Net.BCrypt.Verify(password, candidate.PasswordHash))
            return null;

        return await RequestAsync(candidate.Id, CreateToken(candidate), method, ct);
    }

    public async Task<RegistrationOtpResult> RegisterAsync(User account, string method, CancellationToken ct)
    {
        if (ValidateMethod(account, method) is { } invalid) return invalid;
        var code = codes.GenerateCode();
        VerificationCode challenge;
        // Create the pending account and reserve quota together. A legacy route can never see
        // a choice registration without its marker.
        try
        {
            await using var tx = await BeginAsync(ct);
            db.Users.Add(account);
            await db.SaveChangesAsync(ct);
            challenge = BuildChallenge(account, method, code, clock.GetUtcNow().UtcDateTime);
            db.VerificationCodes.Add(challenge);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (Exception error) when (DatabaseCompatibility.IsConflict(error, sqliteUnique: true, sqlitePrimaryKey: true))
        {
            return Conflict();
        }

        return await DeliverAsync(account, challenge, code, CreateToken(account), ct);
    }

    public async Task<RegistrationOtpResult> RequestAsync(int userId, string? token, string method, CancellationToken ct)
    {
        if (ResolveToken(userId, token) is not { } claim) return InvalidRequest();
        var clinic = claim.Clinic;
        User account;
        VerificationCode challenge;
        string code;
        try
        {
            await using var tx = await BeginAsync(ct);
            var pending = await LockPendingUserAsync(clinic, userId, ct);
            if (pending is null || !StringComparer.Ordinal.Equals(claim.Binding, AccountBinding(pending)))
                return InvalidRequest();
            account = pending;
            var now = clock.GetUtcNow().UtcDateTime;
            var own = ForAccount(userId);
            var latest = await own.MaxAsync(x => (DateTime?)x.CreatedAt, ct);
            if (!latest.HasValue) return InvalidRequest();
            if (ValidateMethod(account, method) is { } invalid) return invalid;
            var remaining = SecondsUntil(latest.Value.AddSeconds(Cooldown), now);
            if (remaining > 0)
            {
                var acceptedMethod = await own.Where(x => x.Purpose == Accepted && !x.IsUsed && x.ExpiresAt > now)
                    .Select(x => x.Channel).FirstOrDefaultAsync(ct);
                return new(429, new
                {
                    error = "Дахин код авахын өмнө түр хүлээнэ үү.",
                    reason = "retry_cooldown", retryAfterSeconds = remaining,
                    userId, registrationToken = token, verificationMethod = acceptedMethod
                }, remaining);
            }
            if (await own.CountAsync(x => x.CreatedAt > now.AddHours(-24), ct) >= DailyLimit ||
                await RecentFailedAttempts(own, now, ct) >= AttemptLimit)
                return AttemptLimitReached();

            code = codes.GenerateCode();
            challenge = BuildChallenge(account, method, code, now);
            db.VerificationCodes.Add(challenge);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (Exception error) when (DatabaseCompatibility.IsConflict(error, sqliteUnique: true))
        {
            return Conflict();
        }

        return await DeliverAsync(account, challenge, code, token!, ct);
    }

    private static int SecondsUntil(DateTime end, DateTime now) =>
        Math.Max(0, (int)Math.Ceiling((end - now).TotalSeconds));

    // A failure on a recently expired/invalidated OTP still counts. Keep a conservative
    // rolling interval of 15 minutes from the last possible attempt (the OTP expiry).
    private static async Task<int> RecentFailedAttempts(
        IQueryable<VerificationCode> records, DateTime now, CancellationToken ct) =>
        await records.Where(x => x.ExpiresAt > now.AddMinutes(-15))
            .SumAsync(x => (int?)x.FailedAttempts, ct) ?? 0;

    private static RegistrationOtpResult AttemptLimitReached() =>
        Error(429, "otp_limit", "Баталгаажуулалтын оролдлогын хязгаарт хүрлээ. Дараа дахин оролдоно уу.");

    private async Task<RegistrationOtpResult> DeliverAsync(User account, VerificationCode challenge,
        string code, string token, CancellationToken ct)
    {
        var destination = Destination(account, challenge.Channel)!;
        var userId = account.Id;
        var key = "registration-" + Guid.NewGuid().ToString("N");
        try
        {
            using var sendDeadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            sendDeadline.CancelAfter(TimeSpan.FromSeconds(15));
            if (challenge.Channel == "email")
                await email.SendAsync(destination, "SuvdDent | Бүртгэл баталгаажуулах код",
                    $"Таны баталгаажуулах код: {code}\n\nКод 120 секунд хүчинтэй.\n" +
                    "Хэрэв та хүсэлт гаргаагүй бол энэ имэйлийг алгасна уу.",
                    key + "@notifications.nexora.invalid", sendDeadline.Token);
            else
                await sms.SendAsync(destination, code, key, sendDeadline.Token);

            using var recordDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            if (await AcceptDeliveryAsync(account.ClinicId, userId, challenge.Id, destination, recordDeadline.Token))
                return new(200, new
                {
                    message = "Баталгаажуулах кодын илгээлтийг хүлээн авлаа.",
                    userId, registrationToken = token, retryAfterSeconds = Cooldown,
                    verificationMethod = challenge.Channel
                });
        }
        catch (Exception)
        {
            // Unaccepted delivery stays unconfirmable and still counts against quota.
            // Never include contact values, provider diagnostics, or codes in logs or API errors.
        }

        return new(503, new
        {
            error = "Код илгээхийг баталгаажуулж чадсангүй. Дараа дахин хүснэ үү.",
            reason = "delivery_unavailable", userId, registrationToken = token,
            retryAfterSeconds = Cooldown
        });
    }

    private async Task<bool> AcceptDeliveryAsync(int clinic, int userId, int challengeId, string destination,
        CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        await using var tx = await BeginAsync(ct);
        var user = await LockPendingUserAsync(clinic, userId, ct);
        if (user is null) return false;
        var own = ForAccount(userId);
        var latest = await own.OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
        if (latest is null || latest.Id != challengeId || latest.Purpose != Delivering ||
            latest.IsUsed || latest.ExpiresAt <= clock.GetUtcNow().UtcDateTime ||
            Destination(user, latest.Channel) != destination) return false;

        // The old accepted OTP remains valid until this transaction commits.
        await own.Where(x => x.Id != challengeId && !x.IsUsed).ExecuteUpdateAsync(
            setters => setters.SetProperty(x => x.IsUsed, true), ct);
        latest.Purpose = Accepted;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<RegistrationOtpResult> ConfirmAsync(int userId, string? token, string? code, CancellationToken ct)
    {
        if (ResolveToken(userId, token) is not { } claim) return InvalidRequest();
        var clinic = claim.Clinic;
        if (code is not { Length: 6 } || !code.All(char.IsAsciiDigit))
            return Error(400, "invalid_code", "6 оронтой код оруулна уу.");

        try
        {
            await using var tx = await BeginAsync(ct);
            var user = await LockPendingUserAsync(clinic, userId, ct);
            if (user is null || !StringComparer.Ordinal.Equals(claim.Binding, AccountBinding(user)))
                return InvalidRequest();
            var own = ForAccount(userId);
            var record = await own.Where(x => x.Purpose == Accepted && !x.IsUsed)
                .OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
            if (record is null) return Error(400, "invalid_code", "Баталгаажуулах код буруу байна.");
            var now = clock.GetUtcNow().UtcDateTime;
            if (record.ExpiresAt <= now)
                return Error(400, "expired_code", "Кодын хугацаа дууссан. Шинэ код авна уу.");
            if (await RecentFailedAttempts(own, now, ct) >= AttemptLimit) return AttemptLimitReached();

            if (!codes.VerifyCode(code, record.CodeHash))
            {
                record.FailedAttempts++;
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return Error(400, "invalid_code", "Баталгаажуулах код буруу байна.");
            }

            record.IsUsed = true;
            user.IsEmailVerified = record.Channel == "email";
            user.IsPhoneVerified = record.Channel == "sms";
            user.IsActive = true;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new(200, new { message = "Бүртгэл амжилттай баталгаажлаа", userId });
        }
        catch (Exception error) when (DatabaseCompatibility.IsConflict(error, sqliteUnique: true))
        {
            return Conflict();
        }
    }

    public IQueryable<int> ChoiceAccounts => db.VerificationCodes
        .Where(x => x.Purpose == Accepted || x.Purpose == Delivering).Select(x => x.UserId);
}
