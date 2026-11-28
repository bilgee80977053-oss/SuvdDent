using DentalApi.Clinical;
using DentalApi.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DentalApi.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Doctor")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/clinical/appointments/{appointmentId:int}/patient-history")]
public sealed class PatientHistoryAcceptanceController(
    IConfiguration configuration, IWebHostEnvironment environment, AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(int appointmentId, CancellationToken token,
        [FromQuery] string? from, [FromQuery] string? until, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        if (!string.Equals(configuration["Features:PatientHistoryAcceptance"], "true", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(configuration["Features:PerformedProcedureAcceptance"], "true", StringComparison.OrdinalIgnoreCase) ||
            !PerformedProcedureService.IsTestingMemorySqlite(environment, db))
            return NotFound();

        var reader = HttpContext.RequestServices.GetRequiredService<PatientPeriodHistoryService>();
        var result = await reader.ListAsync(User, appointmentId, from, until, page, pageSize, token);
        return result.Status switch
        {
            PerformedProcedureReadStatus.Read => Ok(result.Page),
            PerformedProcedureReadStatus.Invalid => BadRequest(new { message = "Огнооны хүрээ эсвэл хуудасны хэмжээ буруу байна." }),
            PerformedProcedureReadStatus.Unavailable => NotFound(),
            _ => StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = "Үзлэгийн түүхийн хандалтыг баталгаажуулж чадсангүй." })
        };
    }
}
