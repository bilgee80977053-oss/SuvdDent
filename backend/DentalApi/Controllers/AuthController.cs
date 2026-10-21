
using System.Net.Mail;
using AppUser = DentalApi.Models.User;
using DentalApi.Data;
using DentalApi.Models;
using DentalApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace DentalApi.Controllers;

public record RegisterRequest(
    string FullName,
    string? Phone,
    string? Email,
    string Password,
    int ClinicId = 1
);

public record LoginRequest(
    string Identifier,
    string Password,
    int ClinicId = 1
);

public record VerifyRequest(
    int UserId,
    string Code
);

public record ResendCodeRequest(
    string Email,
    int ClinicId = 1
);

public record AuthResponse(
    string Token,
    int UserId,
    string FullName,
    string Role,
    int ClinicId
);

[ApiController]
[Route("api/[controller]")]
public class AuthController(
    AppDbContext db,
    JwtService jwt,
    VerificationService verification,
    EmailService emailService
) : ControllerBase
{
    [EnableRateLimiting("auth")]
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest r)
    {
        var phone = r.Phone?.Trim();
        var email = r.Email?.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(r.FullName))
            return BadRequest(new { error = "Нэр шаардлагатай" });

        if (string.IsNullOrWhiteSpace(phone) &&
            string.IsNullOrWhiteSpace(email))
            return BadRequest(new
            {
                error = "Утас эсвэл имэйл оруулна уу"
            });

        if (!string.IsNullOrWhiteSpace(phone) &&
            (phone.Length < 8 || !phone.All(char.IsDigit)))
            return BadRequest(new { error = "Утасны дугаар буруу" });

        if (!string.IsNullOrWhiteSpace(email) &&
            (!MailAddress.TryCreate(email, out var address) ||
             address.Address != email))
            return BadRequest(new { error = "Имэйл хаяг буруу" });

        if (string.IsNullOrEmpty(r.Password) ||
            r.Password.Length < 8)
            return BadRequest(new
            {
                error = "Нууц үг 8-аас дээш тэмдэгттэй байна"
            });

        if (!await db.Clinics.AnyAsync(c => c.Id == r.ClinicId))
            return BadRequest(new { error = "Эмнэлэг олдсонгүй" });

        if (!string.IsNullOrWhiteSpace(phone) &&
            await db.Users.AnyAsync(u =>
                u.ClinicId == r.ClinicId && u.Phone == phone))
            return Conflict(new
            {
                error = "Утас бүртгэлтэй байна"
            });

        if (!string.IsNullOrWhiteSpace(email) &&
            await db.Users.AnyAsync(u =>
                u.ClinicId == r.ClinicId && u.Email == email))
            return Conflict(new
            {
                error = "Имэйл бүртгэлтэй байна"
            });

        var user = new AppUser
        {
            ClinicId = r.ClinicId,
            FullName = r.FullName.Trim(),
            Phone = string.IsNullOrWhiteSpace(phone) ? null : phone,
            Email = string.IsNullOrWhiteSpace(email) ? null : email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(r.Password),
            Role = Role.Patient,
            IsPhoneVerified = false,
            IsEmailVerified = false,
            IsActive = false,
            CreatedAt = DateTime.UtcNow
        };

        var code = verification.GenerateCode();
        var now = DateTime.UtcNow;

        await using (var transaction =
            await db.Database.BeginTransactionAsync())
        {
            db.Users.Add(user);
            await db.SaveChangesAsync();

            db.VerificationCodes.Add(new VerificationCode
            {
                UserId = user.Id,
                Channel = !string.IsNullOrWhiteSpace(user.Email)
                    ? "email"
                    : "sms",
                CodeHash = verification.HashCode(code),
                CreatedAt = now,
                ExpiresAt = now.AddSeconds(120),
                IsUsed = false,
                FailedAttempts = 0
            });

            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            try
            {
                await emailService.SendAsync(
                    user.Email,
                    "Nexora Platform | Бүртгэл баталгаажуулах код",
                    $"Сайн байна уу, {user.FullName}!\n\n" +
                    $"Таны баталгаажуулах код: {code}\n\n" +
                    "Код 120 секунд хүчинтэй.\n" +
                    "Та энэ имэйлд хариу бичих шаардлагагүй.\n" +
                    "Хэрэв та хүсэлт гаргаагүй бол энэ имэйлийг алгасна уу.\n\n" +
                    "Хүндэтгэсэн,\nNexora Platform"
                );
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"SMTP төрөл: {ex.GetType().Name}");

                return StatusCode(503, new
                {
                    error = "Имэйл илгээхэд алдаа гарлаа. " +
                            "Бүртгэл идэвхжээгүй. " +
                            "Дараа нь код дахин хүсэх боломжтой."
                });
            }
        }

        return Ok(new
        {
            message = "Бүртгэлийн хүсэлт үүслээ. Баталгаажуулалт шаардлагатай.",
            userId = user.Id
        });
    }

    // Өмнө бүртгэгдсэн имэйл рүү шинэ код илгээх
    [EnableRateLimiting("auth")]
    [HttpPost("resend-code")]
    public async Task<IActionResult> ResendCode(ResendCodeRequest r)
    {
        var email = r.Email?.Trim().ToLowerInvariant();

        var response = new
        {
            message = "Баталгаажуулалт шаардлагатай бол шинэ код илгээх хүсэлтийг боловсруулна."
        };

        if (string.IsNullOrWhiteSpace(email) ||
            !MailAddress.TryCreate(email, out var address) ||
            address.Address != email)
            return Ok(response);

        var user = await db.Users.FirstOrDefaultAsync(u =>
            u.ClinicId == r.ClinicId &&
            u.Email == email &&
            !u.IsActive &&
            !u.IsEmailVerified);

        if (user is null)
            return Ok(response);

        var now = DateTime.UtcNow;

        var latest = await db.VerificationCodes
            .Where(v => v.UserId == user.Id &&
                        v.Channel == "email")
            .OrderByDescending(v => v.CreatedAt)
            .FirstOrDefaultAsync();

        // 60 секундийн дотор дахин код үүсгэхгүй
        if (latest is not null &&
            latest.CreatedAt.AddSeconds(60) > now)
            return Ok(response);

        var code = verification.GenerateCode();

        // Шинэ кодыг хадгалж, хуучин кодыг хүчингүй болгоно
        await using (var transaction =
            await db.Database.BeginTransactionAsync())
        {
            await db.VerificationCodes
                .Where(v =>
                    v.UserId == user.Id &&
                    v.Channel == "email" &&
                    !v.IsUsed)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(v => v.IsUsed, true));

            db.VerificationCodes.Add(new VerificationCode
            {
                UserId = user.Id,
                Channel = "email",
                CodeHash = verification.HashCode(code),
                CreatedAt = now,
                ExpiresAt = now.AddMinutes(5),
                IsUsed = false,
                FailedAttempts = 0
            });

            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        try
        {
            await emailService.SendAsync(
                user.Email!,
                "Nexora Platform | Шинэ баталгаажуулах код",
                $"Сайн байна уу, {user.FullName}!\n\n" +
                $"Таны шинэ баталгаажуулах код: {code}\n\n" +
                "Код 120 секунд хүчинтэй.\n" +
                "Өмнөх баталгаажуулах код хүчингүй болсон.\n" +
                "Та энэ имэйлд хариу бичих шаардлагагүй.\n" +
                "Хэрэв та хүсэлт гаргаагүй бол энэ имэйлийг алгасна уу.\n\n" +
                "Хүндэтгэсэн,\nNexora Platform"
            );
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"SMTP төрөл: {ex.GetType().Name}");

            // Баталгаажуулалт идэвхгүй хэвээр үлдэнэ.
            // Дараагийн хүсэлтээр шинэ код авч болно.
            return StatusCode(503, new
            {
                error = "Имэйл үйлчилгээ түр ажиллахгүй байна."
            });
        }

        return Ok(response);
    }

    [EnableRateLimiting("auth")]
    [HttpPost("verify")]
    public async Task<IActionResult> Verify(VerifyRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Code) ||
            r.Code.Length != 6 ||
            !r.Code.All(char.IsDigit))
            return BadRequest(new
            {
                error = "6 оронтой код оруулна уу"
            });

        await using var transaction =
            await db.Database.BeginTransactionAsync();

        var user = await db.Users.FindAsync(r.UserId);

        if (user is null || user.IsActive)
            return BadRequest(new
            {
                error = "Баталгаажуулах боломжгүй"
            });

        var record = await db.VerificationCodes
            .Where(v => v.UserId == r.UserId && !v.IsUsed)
            .OrderByDescending(v => v.CreatedAt)
            .FirstOrDefaultAsync();

        if (record is null)
            return BadRequest(new
            {
                error = "Баталгаажуулах код олдсонгүй"
            });

        if (DateTime.UtcNow >= record.ExpiresAt)
            return BadRequest(new
            {
                error = "Кодын хугацаа дууссан"
            });

        if (record.FailedAttempts >= 5)
            return BadRequest(new
            {
                error = "Оролдлогын тоо хэтэрсэн"
            });

        if (record.Channel != "email" &&
            record.Channel != "sms")
            return BadRequest(new
            {
                error = "Баталгаажуулах суваг буруу"
            });

        if (!verification.VerifyCode(r.Code, record.CodeHash))
        {
            await db.VerificationCodes
                .Where(v =>
                    v.Id == record.Id &&
                    !v.IsUsed &&
                    v.FailedAttempts < 5 &&
                    v.ExpiresAt > DateTime.UtcNow)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(
                        v => v.FailedAttempts,
                        v => v.FailedAttempts + 1));

            await transaction.CommitAsync();

            return BadRequest(new
            {
                error = "Баталгаажуулах код буруу"
            });
        }

        var updated = await db.VerificationCodes
            .Where(v =>
                v.Id == record.Id &&
                !v.IsUsed &&
                v.FailedAttempts < 5 &&
                v.ExpiresAt > DateTime.UtcNow)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(v => v.IsUsed, true));

        if (updated != 1)
            return BadRequest(new
            {
                error = "Кодыг ашиглах боломжгүй"
            });

        if (record.Channel == "email")
            user.IsEmailVerified = true;
        else
            user.IsPhoneVerified = true;

        user.IsActive = true;

        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        return Ok(new
        {
            message = "Бүртгэл амжилттай баталгаажлаа",
            userId = user.Id
        });
    }

    [EnableRateLimiting("auth")]
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest r)
    {
        var identifier = (r.Identifier ?? "").Trim();
        var email = identifier.ToLowerInvariant();

        var user = await db.Users.FirstOrDefaultAsync(u =>
            u.ClinicId == r.ClinicId &&
            (u.Phone == identifier || u.Email == email) &&
            u.IsActive);

        if (user is null ||
            !BCrypt.Net.BCrypt.Verify(
                r.Password ?? "", user.PasswordHash))
            return Unauthorized(new
            {
                error = "Нэвтрэх мэдээлэл буруу эсвэл бүртгэл идэвхгүй байна"
            });

        return Ok(new AuthResponse(
            jwt.Create(user),
            user.Id,
            user.FullName,
            user.Role.ToString(),
            user.ClinicId
        ));
    }

    [Authorize]
    [HttpGet("me")]
    public IActionResult Me() => Ok(new
    {
        userId = User.FindFirst("sub")?.Value,
        name = User.FindFirst("name")?.Value,
        role = User.FindFirst("role")?.Value,
        clinicId = User.FindFirst("clinicId")?.Value
    });
}
