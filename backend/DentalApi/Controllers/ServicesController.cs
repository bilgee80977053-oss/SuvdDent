using DentalApi.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DentalApi.Controllers;

public record ServiceDto(int Id, string Name, string Description, string? ImagePath,
                         long MinPrice, long? MaxPrice, string? AftercareText);

[ApiController]
[Route("api/[controller]")]
public class ServicesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ServiceDto>>> Get([FromQuery] int clinicId = 1)
    {
        var list = await db.Services
            .Where(s => s.ClinicId == clinicId && s.IsActive)
            .OrderBy(s => s.Name)
            .Select(s => new ServiceDto(s.Id, s.Name, s.Description, s.ImagePath,
                                        s.MinPrice, s.MaxPrice, s.AftercareText))
            .ToListAsync();
        return Ok(list);
    }
}