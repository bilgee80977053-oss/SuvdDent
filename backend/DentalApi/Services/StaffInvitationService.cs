using System.Net.Mail;
using System.Security.Cryptography;
using DentalApi.Contracts;
using DentalApi.Data;
using DentalApi.Models;
using Microsoft.EntityFrameworkCore;

namespace DentalApi.Services;

public interface IStaffInvitationSender
{
    Task SendAsync(string email, string code, string clinicName, CancellationToken ct);
}

public sealed class StaffInvitationEmailSender(EmailService email) : IStaffInvitationSender
{
    public Task SendAsync(string address, string code, string clinicName, CancellationToken ct) =>
        email.SendAsync(
            address,
            "Suvd Dent | Эмчийн бүртгэлийн урилга",
            $"Таныг {clinicName} эмнэлэгт эмчээр ажиллах урилга ирүүллээ.\n" +
            $"Баталгаажуулах код: {code}\n" +
            "Код 15 минут хүчинтэй. Хэрэв та энэ урилгыг хүсээгүй бол үл тооно уу.\n" +
            "Код болон нууц үгээ бусдад бүү дамжуулаарай.",
            cancellationToken: ct);
}

public record StaffInvitationResult(int Status, object Response);

/// A Doctor's credentials are never selected by the Director. An account is
/// inactive until the invitee verifies the email and sets their own password.
public sealed class StaffInvitationService(
    AppDbContext db,
    IStaffInvitationSender sender,
    VerificationService verification)
{
    private const string Purpose = "doctor-invite";
    private static readonly object Accepted = new { message =
        "Урилгыг хүлээн авлаа. Боломжтой бол имэйлээр баталгаажуулах код хүргэнэ." };
    private static readonly object Invalid = new { error = "Код буруу эсвэл хугацаа дууссан." };

    public async Task<DirectorStaffResponse[]> ListAsync(int clinicId, CancellationToken ct)
    {
        var accounts = await db.Users.AsNoTracking()
            .Where(u => u.ClinicId == clinicId && u.Role == Role.Doctor)
            .OrderBy(u => u.FullName).ThenBy(u => u.Id)
            .Select(u => new
            {
                u.Id, u.FullName, u.Email, u.Role, u.IsActive, u.IsEmailVerified
            })
            .ToArrayAsync(ct);
        return accounts.Select(u => new DirectorStaffResponse(
            u.Id, u.FullName, u.Email ?? "", u.Role.ToString(),
            u.IsActive, u.IsEmailVerified)).ToArray();
    }

    public async Task<StaffInvitationResult> InviteAsync(
        int clinicId, InviteDoctorRequest input, CancellationToken ct)
    {
        var name = input.FullName?.Trim() ?? "";
        var email = input.Email?.Trim().ToLowerInvariant() ?? "";
        if (name.Length < 2 || name.Length > 200 ||
            email.Length > 254 || !MailAddress.TryCreate(email, out var parsed) ||
            parsed.Address != email)
            return new(400, new { error = "Эмчийн нэр, имэйлийг шалгана уу." });

        var clinicName = await db.Clinics.AsNoTracking()
            .Where(c => c.Id == clinicId).Select(c => c.Name)
            .SingleOrDefaultAsync(ct);
        if (clinicName is null) return new(404, new { error = "Эмнэлэг олдсонгүй." });

        string code;
        try
        {
            await using var tx = await DatabaseCompatibility.BeginWriteAsync(db);
            var existing = await db.Users
                .SingleOrDefaultAsync(u => u.ClinicId == clinicId && u.Email == email, ct);
            User user;
            if (existing is null)
            {
                user = new User
                {
                    ClinicId = clinicId,
                    FullName = name,
                    Email = email,
                    Role = Role.Doctor,
                    IsActive = false,
                    IsEmailVerified = false,
                    CreatedAt = DateTime.UtcNow,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(
                        Convert.ToHexString(RandomNumberGenerator.GetBytes(32)))
                };
                db.Users.Add(user);
                await db.SaveChangesAsync(ct);
            }
            else
            {
                // Never convert an existing Patient or another staff role.
                if (existing.Role != Role.Doctor || existing.IsActive ||
                    existing.IsEmailVerified || existing.FullName != name)
                    return new(409, new { error =
                        "Энэ имэйлээр бүртгэл байгаа эсвэл урилга илгээх боломжгүй." });
                user = existing;
            }

            var latest = await db.VerificationCodes
                .Where(x => x.UserId == user.Id && x.Purpose == Purpose)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync(ct);
            if (latest is not null && latest.CreatedAt.AddSeconds(60) > DateTime.UtcNow)
                return new(429, new { error = "Урилга дахин илгээхээс өмнө түр хүлээнэ үү." });

            code = RandomNumberGenerator.GetInt32(0, 100000000).ToString("D8");
            await db.VerificationCodes
                .Where(x => x.UserId == user.Id && x.Purpose == Purpose && !x.IsUsed)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsUsed, true), ct);
            db.VerificationCodes.Add(new VerificationCode
            {
                UserId = user.Id,
                Channel = "email",
                Purpose = Purpose,
                CodeHash = verification.HashCode(code),
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddMinutes(15)
            });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (Exception e) when (DatabaseCompatibility.IsConflict(e, sqliteUnique: true))
        {
            return new(409, new { error = "Бүртгэл зэрэг өөрчлөгдсөн байна." });
        }

        try
        {
            await sender.SendAsync(email, code, clinicName, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch
        {
            // Account remains inactive. Director may retry after cooldown;
            // a code is NEVER returned to the Director or written to logs.
            return new(503, new { error =
                "Урилгын имэйл илгээгдсэн нь баталгаажаагүй. Дараа дахин оролдоно уу." });
        }
        return new(202, Accepted);
    }

    public async Task<StaffInvitationResult> AcceptAsync(
        AcceptDoctorInvitationRequest input, CancellationToken ct)
    {
        var email = input.Email?.Trim().ToLowerInvariant() ?? "";
        var code = input.Code ?? "";
        var password = input.NewPassword ?? "";
        if (input.ClinicId <= 0 || !MailAddress.TryCreate(email, out var parsed) ||
            parsed.Address != email || code.Length != 8 || !code.All(char.IsDigit) ||
            password.Length < 12 || password.Length > 128)
            return new(400, Invalid);

        try
        {
            await using var tx = await DatabaseCompatibility.BeginWriteAsync(db);
            var user = await db.Users.SingleOrDefaultAsync(
                u => u.ClinicId == input.ClinicId && u.Email == email &&
                    u.Role == Role.Doctor && !u.IsActive && !u.IsEmailVerified, ct);
            if (user is null) return new(400, Invalid);

            var record = await db.VerificationCodes
                .Where(x => x.UserId == user.Id && x.Channel == "email" &&
                    x.Purpose == Purpose && !x.IsUsed)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync(ct);
            if (record is null || record.ExpiresAt <= DateTime.UtcNow ||
                record.FailedAttempts >= 5) return new(400, Invalid);

            if (!verification.VerifyCode(code, record.CodeHash))
            {
                await db.VerificationCodes.Where(x =>
                        x.Id == record.Id && !x.IsUsed &&
                        x.FailedAttempts < 5 && x.ExpiresAt > DateTime.UtcNow)
                    .ExecuteUpdateAsync(s => s.SetProperty(
                        x => x.FailedAttempts, x => x.FailedAttempts + 1), ct);
                await tx.CommitAsync(ct);
                return new(400, Invalid);
            }

            var marked = await db.VerificationCodes.Where(x =>
                    x.Id == record.Id && !x.IsUsed &&
                    x.FailedAttempts < 5 && x.ExpiresAt > DateTime.UtcNow)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsUsed, true), ct);
            if (marked != 1) return new(400, Invalid);

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
            user.IsEmailVerified = true;
            user.IsActive = true;
            user.TokenVersion++;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new(200, new { message = "Эмчийн бүртгэл баталгаажлаа. Одоо нэвтэрч болно." });
        }
        catch (Exception e) when (DatabaseCompatibility.IsConflict(e, sqliteUnique: true))
        {
            return new(409, Invalid);
        }
    }
}
