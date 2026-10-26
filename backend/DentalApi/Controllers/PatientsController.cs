
using DentalApi.Data;
using DentalApi.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DentalApi.Controllers;

// Өвчтөний бүртгэл үүсгэх хүсэлт
public record CreateMyPatientRequest(
    string FullName,
    string Phone,
    DateTime? DateOfBirth,
    string? Address
);

// Өвчтөний мэдээлэл засах хүсэлт
public record UpdateMyPatientRequest(
    string FullName,
    string Phone,
    DateTime? DateOfBirth,
    string? Address
);

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Patient")]
public class PatientsController(AppDbContext db) : ControllerBase
{
    // 1. Өөрийн мэдээллийг харах
    [HttpGet("me")]
    public async Task<IActionResult> GetMyProfile()
    {
        if (!TryGetIdentity(out var userId, out var clinicId))
            return Forbid();

        var patient = await db.Patients
            .AsNoTracking()
            .Where(p =>
                p.UserId == userId &&
                p.ClinicId == clinicId &&
                p.IsActive)
            .Select(p => new
            {
                p.Id,
                p.FullName,
                p.Phone,
                p.DateOfBirth,
                p.Address
            })
            .SingleOrDefaultAsync();

        if (patient is null)
        {
            return NotFound(new
            {
                message = "Таны өвчтөний бүртгэл олдсонгүй."
            });
        }

        return Ok(patient);
    }

    // 2. Өөрийн бүртгэлийг үүсгэх
    [HttpPost("me")]
    public async Task<IActionResult> CreateMyProfile(
        [FromBody] CreateMyPatientRequest request)
    {
        if (!TryGetIdentity(out var userId, out var clinicId))
            return Forbid();

        if (string.IsNullOrWhiteSpace(request.FullName) ||
            string.IsNullOrWhiteSpace(request.Phone))
        {
            return BadRequest(new
            {
                message = "Нэр болон утасны дугаар шаардлагатай."
            });
        }

        var validUser = await IsValidPatientUser(userId, clinicId);

        if (!validUser)
            return Forbid();

        var exists = await db.Patients.AnyAsync(p =>
            p.UserId == userId &&
            p.ClinicId == clinicId);

        if (exists)
        {
            return Conflict(new
            {
                message = "Таны өвчтөний бүртгэл аль хэдийн байна."
            });
        }

        var patient = new Patient
        {
            UserId = userId,
            ClinicId = clinicId,
            FullName = request.FullName.Trim(),
            Phone = request.Phone.Trim(),
            DateOfBirth = request.DateOfBirth,
            Address = request.Address?.Trim(),
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };

        db.Patients.Add(patient);
        await db.SaveChangesAsync();

        return CreatedAtAction(
            nameof(GetMyProfile),
            new
            {
                patient.Id,
                patient.FullName,
                patient.Phone
            });
    }

    // 3. Өөрийн мэдээллийг засах
    [HttpPut("me")]
    public async Task<IActionResult> UpdateMyProfile(
        [FromBody] UpdateMyPatientRequest request)
    {
        if (!TryGetIdentity(out var userId, out var clinicId))
            return Forbid();

        if (string.IsNullOrWhiteSpace(request.FullName) ||
            string.IsNullOrWhiteSpace(request.Phone))
        {
            return BadRequest(new
            {
                message = "Нэр болон утасны дугаар шаардлагатай."
            });
        }

        var validUser = await IsValidPatientUser(userId, clinicId);

        if (!validUser)
            return Forbid();

        // Зөвхөн өөрийн идэвхтэй бүртгэлийг хайх
        var patient = await db.Patients.SingleOrDefaultAsync(p =>
            p.UserId == userId &&
            p.ClinicId == clinicId &&
            p.IsActive);

        if (patient is null)
        {
            return NotFound(new
            {
                message = "Өвчтөний бүртгэл олдсонгүй."
            });
        }

        patient.FullName = request.FullName.Trim();
        patient.Phone = request.Phone.Trim();
        patient.DateOfBirth = request.DateOfBirth;
        patient.Address = request.Address?.Trim();

        await db.SaveChangesAsync();

        return Ok(new
        {
            patient.Id,
            patient.FullName,
            patient.Phone,
            patient.DateOfBirth,
            patient.Address
        });
    }

    // Хэрэглэгч идэвхтэй Patient эсэхийг шалгах
    private async Task<bool> IsValidPatientUser(
        int userId,
        int clinicId)
    {
        return await db.Users.AnyAsync(u =>
            u.Id == userId &&
            u.ClinicId == clinicId &&
            u.IsActive &&
            u.Role == Role.Patient);
    }

    // JWT токеноос UserId, ClinicId авах
    private bool TryGetIdentity(
        out int userId,
        out int clinicId)
    {
        var userIdText = User.FindFirst("userId")?.Value;
        var clinicIdText = User.FindFirst("clinicId")?.Value;

        bool validUserId = int.TryParse(userIdText, out userId);
        bool validClinicId = int.TryParse(clinicIdText, out clinicId);

        return validUserId &&
               validClinicId &&
               userId > 0 &&
               clinicId > 0;
    }
}
