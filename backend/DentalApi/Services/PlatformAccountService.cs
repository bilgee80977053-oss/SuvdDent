using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;
using DentalApi.Contracts;
using DentalApi.Data;
using DentalApi.Models;
using Microsoft.EntityFrameworkCore;

namespace DentalApi.Services;

public sealed class PlatformAccountService(AppDbContext db, PlatformJwtService jwt,
    VerificationService codes, IPlatformMessageSender messages)
{
    private static readonly object Generic = new { message = "If eligible, verification instructions will be sent." };
    private static readonly string DummyHash = BCrypt.Net.BCrypt.HashPassword("Synthetic-not-a-user-password-01", workFactor: 12);
    private static (string Channel, string Value)? Contact(string? input)
    {
        var value = input?.Trim();
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value.Contains('@'))
        {
            value = value.ToLowerInvariant();
            return value.Length <= 254 && MailAddress.TryCreate(value, out var parsed) && parsed.Address == value
                ? ("email", value) : null;
        }
        if (Regex.IsMatch(value, @"\A[0-9]{8}\z")) value = "+976" + value;
        return Regex.IsMatch(value, @"\A[+]976[0-9]{8}\z") ? ("sms", value) : null;
    }
    private static bool PasswordValid(string password) => !string.IsNullOrWhiteSpace(password) &&
        password.Length >= 12 && Encoding.UTF8.GetByteCount(password) <= 72;
    private Task<PlatformAccount?> Find((string Channel, string Value) contact) => db.PlatformAccounts
        .SingleOrDefaultAsync(a => contact.Channel == "email" ? a.Email == contact.Value : a.Phone == contact.Value);
    private static bool Verified(PlatformAccount a, string channel) => channel == "email" ? a.IsEmailVerified : a.IsPhoneVerified;
    private static PlatformAccountResponse Response(PlatformAccount a) => new(a.Id, a.FullName, a.Email, a.Phone, a.IsEmailVerified, a.IsPhoneVerified);
    public async Task<SchedulingResult> Register(PlatformRegisterRequest request)
    {
        var email = Contact(request.Email); var phone = Contact(request.Phone);
        if (string.IsNullOrWhiteSpace(request.FullName) || request.FullName.Trim().Length > 200 || !PasswordValid(request.Password) ||
            (!string.IsNullOrWhiteSpace(request.Email) && email?.Channel != "email") ||
            (!string.IsNullOrWhiteSpace(request.Phone) && phone?.Channel != "sms") || (email is null && phone is null)) return new(400);
        if (!messages.IsAvailable) return new(503);
        var account = new PlatformAccount { FullName = request.FullName.Trim(), Email = email?.Value,
            Phone = phone?.Value, PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, workFactor: 12) };
        var contact = email ?? phone!.Value;
        var code = codes.GenerateCode();
        try
        {
            await using var tx = await DatabaseCompatibility.BeginWriteAsync(db);
            if (await db.PlatformAccounts.AnyAsync(a => (account.Email != null && a.Email == account.Email) ||
                (account.Phone != null && a.Phone == account.Phone))) return new(409);
            db.PlatformAccounts.Add(account); await db.SaveChangesAsync();
            db.PlatformVerificationCodes.Add(NewCode(account.Id, contact.Channel, "registration", code));
            await db.SaveChangesAsync(); await tx.CommitAsync();
        }
        catch (Exception e) when (DatabaseCompatibility.IsConflict(e, sqliteUnique: true)) { return new(409); }
        return await Deliver(contact, "registration", code);
    }
    private PlatformVerificationCode NewCode(int account, string channel, string purpose, string code) => new()
    {
        PlatformAccountId = account, Channel = channel, Purpose = purpose, CodeHash = codes.HashCode(code),
        CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddMinutes(10)
    };
    private async Task<SchedulingResult> Deliver((string Channel, string Value) contact, string purpose, string code)
    {
        try { await messages.SendAsync(contact.Channel, contact.Value, purpose, code); return new(202, Generic); }
        catch { return new(503, new { error = "Verification delivery unavailable." }); }
    }

    public async Task<SchedulingResult> Challenge(string identifier, bool recovery)
    {
        var contact = Contact(identifier);
        if (!messages.IsAvailable) return new(503);
        if (contact is null) return new(202, Generic);
        var purpose = recovery ? "password-reset" : "registration";
        var code = codes.GenerateCode();
        try
        {
            await using var tx = await DatabaseCompatibility.BeginWriteAsync(db);
            var account = await Find(contact.Value);
            if (account is null || (recovery ? !account.IsActive || !Verified(account, contact.Value.Channel) :
                Verified(account, contact.Value.Channel) || (!account.IsActive && (account.IsEmailVerified || account.IsPhoneVerified))))
                return new(202, Generic);
            var latest = await db.PlatformVerificationCodes.Where(c => c.PlatformAccountId == account.Id &&
                c.Channel == contact.Value.Channel && c.Purpose == purpose).OrderByDescending(c => c.CreatedAt).FirstOrDefaultAsync();
            if (latest is not null && latest.CreatedAt.AddSeconds(60) > DateTime.UtcNow) return new(202, Generic);
            await db.PlatformVerificationCodes.Where(c => c.PlatformAccountId == account.Id && c.Channel == contact.Value.Channel &&
                c.Purpose == purpose && !c.IsUsed).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsUsed, true));
            db.PlatformVerificationCodes.Add(NewCode(account.Id, contact.Value.Channel, purpose, code));
            await db.SaveChangesAsync(); await tx.CommitAsync();
        }
        catch (Exception e) when (DatabaseCompatibility.IsConflict(e)) { return new(409); }
        return await Deliver(contact.Value, purpose, code);
    }

    public async Task<SchedulingResult> Verify(string identifier, string code, string? password = null)
    {
        var contact = Contact(identifier);
        if (contact is null || !Regex.IsMatch(code, @"\A[0-9]{6}\z") || (password is not null && !PasswordValid(password))) return new(400);
        var purpose = password is null ? "registration" : "password-reset";
        try
        {
            await using var tx = await DatabaseCompatibility.BeginWriteAsync(db);
            var a = await Find(contact.Value);
            if (a is null || (password is null ? Verified(a, contact.Value.Channel) ||
                (!a.IsActive && (a.IsEmailVerified || a.IsPhoneVerified)) : !a.IsActive || !Verified(a, contact.Value.Channel))) return new(400);
            var record = await db.PlatformVerificationCodes.Where(c => c.PlatformAccountId == a.Id && c.Channel == contact.Value.Channel &&
                c.Purpose == purpose && !c.IsUsed).OrderByDescending(c => c.CreatedAt).FirstOrDefaultAsync();
            if (record is null || record.ExpiresAt <= DateTime.UtcNow || record.FailedAttempts >= 5) return new(400);
            if (!codes.VerifyCode(code, record.CodeHash))
            {
                record.FailedAttempts++;
                await db.SaveChangesAsync(); await tx.CommitAsync(); return new(400);
            }
            record.IsUsed = true;
            if (password is null)
            {
                if (contact.Value.Channel == "email") a.IsEmailVerified = true; else a.IsPhoneVerified = true;
                a.IsActive = true;
            }
            else
            {
                a.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);
                a.TokenVersion = checked(a.TokenVersion + 1);
                await db.PlatformVerificationCodes.Where(c => c.PlatformAccountId == a.Id && !c.IsUsed)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.IsUsed, true));
            }
            await db.SaveChangesAsync(); await tx.CommitAsync();
            return new(200, new { message = "Account verification or password update completed." });
        }
        catch (Exception e) when (DatabaseCompatibility.IsConflict(e)) { return new(409); }
    }

    public async Task<SchedulingResult> Login(PlatformLoginRequest request)
    {
        var contact = Contact(request.Identifier);
        var a = contact is null ? null : await Find(contact.Value);
        if (Encoding.UTF8.GetByteCount(request.Password) > 72) return new(401);
        var valid = BCrypt.Net.BCrypt.Verify(request.Password, a?.PasswordHash ?? DummyHash);
        if (!valid || a is null || !a.IsActive || !Verified(a, contact!.Value.Channel)) return new(401);
        try { return new(200, new PlatformLoginResponse(jwt.Create(a), Response(a))); }
        catch (InvalidOperationException) { return new(503); }
    }
    public async Task<PlatformAccountResponse?> Me(int id)
    {
        var a = await db.PlatformAccounts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id && a.IsActive);
        return a is null ? null : Response(a);
    }
}
