using DentalApi.Clinical;
using DentalApi.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DentalApi.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Doctor")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/clinical/appointments/assigned")]
public sealed class AssignedEncounterAcceptanceController(
    IConfiguration configuration, IWebHostEnvironment environment, AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken token,
        [FromQuery] string? from, [FromQuery] string? until, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        if (!string.Equals(configuration["Features:AssignedEncounterAcceptance"], "true", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(configuration["Features:PerformedProcedureAcceptance"], "true", StringComparison.OrdinalIgnoreCase) ||
            !PerformedProcedureService.IsTestingMemorySqlite(environment, db))
            return NotFound();

        var reader = HttpContext.RequestServices.GetRequiredService<AssignedEncounterListService>();
        return (await reader.ListAsync(User, from, until, page, pageSize, token)) switch
        {
            { Status: PerformedProcedureReadStatus.Read, Page: { } result } => Ok(result),
            { Status: PerformedProcedureReadStatus.Invalid } => BadRequest(new { message = "Огнооны хүрээ эсвэл хуудасны хэмжээ буруу байна." }),
            { Status: PerformedProcedureReadStatus.Unavailable } => NotFound(),
            _ => StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = "Үзлэгийн жагсаалтын хандалтыг баталгаажуулж чадсангүй." })
        };
    }
}
