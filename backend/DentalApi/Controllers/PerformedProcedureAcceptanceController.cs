using System.Text.Json.Serialization;
using DentalApi.Clinical;
using DentalApi.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DentalApi.Controllers;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SignPerformedProcedureRequest(Guid RequestId,
    [property: JsonNumberHandling(JsonNumberHandling.Strict)] int ServiceId, string? Scope, string? ToothCode);

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Doctor")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/clinical/appointments/{appointmentId:int}/performed-procedures")]
public sealed class PerformedProcedureAcceptanceController(
    IConfiguration configuration, IWebHostEnvironment environment, AppDbContext db) : ControllerBase
{
    private const int MaxRequestBytes = 1024;

    private bool Enabled =>
        string.Equals(configuration["Features:PerformedProcedureAcceptance"], "true", StringComparison.OrdinalIgnoreCase) &&
        PerformedProcedureService.IsTestingMemorySqlite(environment, db);

    [HttpGet]
    public async Task<IActionResult> List(int appointmentId, CancellationToken token,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        if (!Enabled)
            return NotFound();

        var reader = HttpContext.RequestServices.GetRequiredService<PerformedProcedureService>();
        var result = await reader.ListAsync(User, appointmentId, page, pageSize, token);
        return result.Status switch
        {
            PerformedProcedureReadStatus.Read => Ok(result.Page),
            PerformedProcedureReadStatus.Invalid => BadRequest(new { message = "Хуудасны дугаар 1–1000, хэмжээ 1–50 байна." }),
            PerformedProcedureReadStatus.Unavailable => NotFound(),
            _ => StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = "Ажилбарын хандалтын аудитыг баталгаажуулж чадсангүй." })
        };
    }


    [HttpPost]
    [RequestSizeLimit(MaxRequestBytes)]
    public async Task<IActionResult> Sign(int appointmentId, SignPerformedProcedureRequest request, CancellationToken token)
    {
        // This switch is necessary, but never sufficient outside Testing with the active in-memory SQLite store.
        if (!Enabled)
            return NotFound();
        if (Request.ContentLength is > MaxRequestBytes)
            return StatusCode(StatusCodes.Status413PayloadTooLarge);

        var scope = request.Scope switch
        {
            "Visit" => StructuredTreatmentScope.Visit,
            "SingleTooth" => StructuredTreatmentScope.SingleTooth,
            _ => StructuredTreatmentScope.Unspecified
        };
        var writer = HttpContext.RequestServices.GetRequiredService<PerformedProcedureService>();
        var result = await writer.SignAsync(User,
            new(appointmentId, request.ServiceId, scope, request.ToothCode), request.RequestId, token);
        return result.Status switch
        {
            PerformedProcedureStatus.Recorded when result.Receipt is { } signed =>
                StatusCode(StatusCodes.Status201Created, new
                {
                    signed.AppointmentId,
                    signed.RequestId,
                    signed.ServiceId,
                    signed.ServiceLabel,
                    Scope = signed.Target.Scope.ToString(),
                    ToothCode = signed.Target.Tooth?.Code,
                    signed.SignedUtc
                }),
            PerformedProcedureStatus.Replayed => NoContent(),
            PerformedProcedureStatus.Invalid => BadRequest(new { message = "Үзлэг, үйлчилгээ, ажилбарын хүрээ эсвэл хүсэлтийн дугаар буруу байна." }),
            PerformedProcedureStatus.Unavailable => NotFound(),
            PerformedProcedureStatus.Conflict => Conflict(new { message = "Энэ хүсэлтийн дугаараар өөр ажилбар бүртгэгдсэн байна." }),
            _ => StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = "Ажилбар болон аудитын бүртгэлийг баталгаажуулж чадсангүй." })
        };
    }
}
