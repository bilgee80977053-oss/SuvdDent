using AppUser = DentalApi.Models.User;
using DentalApi.Data;
using DentalApi.Models;
using DentalApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DentalApi.Controllers;

public record RegisterRequest(string FullName, string Phone, string Password, int ClinicId = 1);
public record LoginRequest(string Phone, string Password, int ClinicId = 1);
public record AuthResponse(string Token, int UserId, string FullName, string Role, int ClinicId);

[ApiController]
[Route("api/[controller]")]
public class AuthController(AppDbContext db, JwtService jwt) : ControllerBase
{
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest r)
    {
        var phone = (r.Phone ?? "").Trim();
        if (string.IsNullOrWhiteSpace(r.FullName) || phone.Length < 8 || (r.Password ?? "").Length < 8)
            return BadRequest(new { error = "Нэр, утас (8+ орон), нууц үг (8+ тэмдэгт) шаардлагатай" });
        if (!await db.Clinics.AnyAsync(c => c.Id == r.ClinicId))
            return BadRequest(new { error = "Эмнэлэг олдсонгүй" });
        if (await db.Users.AnyAsync(u => u.ClinicId == r.ClinicId && u.Phone == phone))
            return Conflict(new { error = "Энэ утасны дугаар бүртгэлтэй байна" });

        var user = new AppUser
        {
            ClinicId = r.ClinicId,
            FullName = r.FullName.Trim(),
            Phone = phone,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(r.Password),
            Role = Role.Patient
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return Ok(new AuthResponse(jwt.Create(user), user.Id, user.FullName, user.Role.ToString(), user.ClinicId));
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest r)
    {
        var phone = (r.Phone ?? "").Trim();
        var user = await db.Users.FirstOrDefaultAsync(u => u.ClinicId == r.ClinicId && u.Phone == phone && u.IsActive);
        if (user is null || !BCrypt.Net.BCrypt.Verify(r.Password ?? "", user.PasswordHash))
            return Unauthorized(new { error = "Утас эсвэл нууц үг буруу" });
        return Ok(new AuthResponse(jwt.Create(user), user.Id, user.FullName, user.Role.ToString(), user.ClinicId));
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