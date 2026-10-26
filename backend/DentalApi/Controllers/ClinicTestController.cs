
using System.Security.Claims;
using DentalApi.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DentalApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ClinicTestController(AppDbContext db) : ControllerBase
{
    [HttpGet("my-clinic")]
    public async Task<IActionResult> MyClinic()
    {
        var clinicClaim = User.FindFirst("clinicId")?.Value;

        if (!int.TryParse(clinicClaim, out var clinicId))
            return Forbid();

        var clinic = await db.Clinics
            .AsNoTracking()
            .Where(c => c.Id == clinicId)
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.Phone,
                c.WorkHours
            })
            .FirstOrDefaultAsync();

        if (clinic is null)
            return NotFound(new
            {
                error = "Эмнэлэг олдсонгүй"
            });

        return Ok(clinic);
    }
}
