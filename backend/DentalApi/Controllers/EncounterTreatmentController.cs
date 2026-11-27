using System.Text.Json.Serialization;
using DentalApi.Data;
using DentalApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DentalApi.Controllers;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SignEncounterTreatmentRequest(string? Note);

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Doctor")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/clinical/appointments/{appointmentId:int}/treatment")]
public sealed class EncounterTreatmentController(
    IEncounterTreatmentService treatments, IConfiguration configuration, IWebHostEnvironment environment, AppDbContext db)
    : ControllerBase
{
    // First review slice can only be enabled on isolated synthetic SQLite data.
    private bool Enabled
    {
        get
        {
            if (!(environment.IsDevelopment() || environment.IsEnvironment("Testing")) ||
                !string.Equals(configuration["Features:ClinicalTreatment"], "true", StringComparison.OrdinalIgnoreCase) ||
                !db.Database.IsSqlite())
                return false;

            return IsMemorySqliteConnection(db.Database.GetConnectionString());
        }
    }

    internal static bool IsMemorySqliteConnection(string? connectionString)
    {
        try
        {
            var sqlite = new SqliteConnectionStringBuilder(connectionString);
            return sqlite.Mode == SqliteOpenMode.Memory || sqlite.DataSource == ":memory:";
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    [HttpPost]
    [RequestSizeLimit(16 * 1024)]
    public async Task<IActionResult> Sign(
        int appointmentId, SignEncounterTreatmentRequest request, CancellationToken token)
    {
        if (!Enabled)
            return NotFound();

        return await treatments.SignAsync(User, appointmentId, request.Note, token) switch
        {
            EncounterTreatmentOutcome.Recorded => CreatedAtAction(nameof(Read), new { appointmentId }, null),
            EncounterTreatmentOutcome.Invalid => BadRequest(new { message = "Тэмдэглэл 1–2000 тэмдэгттэй байх ёстой." }),
            EncounterTreatmentOutcome.Missing => NotFound(),
            EncounterTreatmentOutcome.Duplicate => Conflict(new { message = "Энэ үзлэгийн тэмдэглэл аль хэдийн баталгаажсан." }),
            _ => StorageUnavailable()
        };
    }

    [HttpGet]
    public async Task<IActionResult> Read(int appointmentId, CancellationToken token)
    {
        if (!Enabled)
            return NotFound();

        var result = await treatments.ReadAsync(User, appointmentId, token);
        return result.Outcome switch
        {
            EncounterTreatmentOutcome.Read => Ok(result.Record),
            EncounterTreatmentOutcome.Missing => NotFound(),
            _ => StorageUnavailable()
        };
    }

    private ObjectResult StorageUnavailable() => StatusCode(StatusCodes.Status503ServiceUnavailable,
        new { message = "Тэмдэглэл эсвэл хандалтын бүртгэлийг баталгаажуулж чадсангүй." });
}
