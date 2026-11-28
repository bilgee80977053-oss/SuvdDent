using DentalApi.Clinical;
using DentalApi.Data;
using DentalApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DentalApi.IntegrationTests;

// This controller is not compiled into DentalApi. Only an explicitly configured
// synthetic host adds this test assembly as an MVC application part.
[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Patient")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/appointments/{appointmentId:int}/invoice-draft-review")]
public sealed class InvoiceDraftReviewFixtureController(
    AppDbContext db, IWebHostEnvironment environment, AppointmentService appointments) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(int appointmentId)
    {
        if (!PerformedProcedureService.IsTestingMemorySqlite(environment, db) || appointmentId <= 0)
            return Unavailable();

        if (!int.TryParse(User.FindFirst("clinicId")?.Value, out var clinicId) ||
            !int.TryParse(User.FindFirst("userId")?.Value, out var userId) || clinicId <= 0 || userId <= 0)
            return Unauthorized();

        // The normal booking read checks the active linked clinic account and
        // patient. Do not resolve prices for callers without that ownership.
        var ownership = await appointments.Read(clinicId, userId, appointmentId);
        if (ownership.Status != 200) return Unavailable();

        var encounter = new InvoiceEncounterIdentity(clinicId, appointmentId);
        var source = HttpContext.RequestServices.GetService<InvoiceDraftReviewFixturePricing>();
        if (source?.ApprovedPricing is not { } pricing || pricing.Encounter != encounter)
            return Unavailable();

        try
        {
            var draft = InvoiceDraftSnapshot.Create(encounter, pricing);
            var breakdown = InvoiceDraftReviewBreakdown.Project(draft);
            return Ok(new { appointmentId, breakdown });
        }
        catch (ArgumentException)
        {
            return Unavailable();
        }
    }

    // Use an explicit generic body; framework ProblemDetails may include a
    // unique trace path/ID and is not the existence-independent contract.
    private NotFoundObjectResult Unavailable() => NotFound(new { message = "Нэхэмжлэхийн төсөл харах боломжгүй." });
}

internal sealed record InvoiceDraftReviewFixturePricing(ServerApprovedInvoicePricing? ApprovedPricing);

internal static class InvoiceDraftReviewFixtureRegistration
{
    internal static void AddInvoiceDraftReviewFixture(this IServiceCollection services) =>
        services.AddInvoiceDraftReviewFixture(new(new(101, 301),
            Guid.Parse("3b5ec033-5e78-4ae7-8997-dc75d503320c"),
            Guid.Parse("5b7d80ae-25f4-4fca-a5c4-b25b5d682815"),
            InvoiceAmountSnapshot.Calculate("XTS", 2, [new(200, 15, 8), new(100, 50, 3)])));

    internal static void AddInvoiceDraftReviewFixture(this IServiceCollection services, ServerApprovedInvoicePricing? pricing)
    {
        services.AddControllers().AddApplicationPart(typeof(InvoiceDraftReviewFixtureController).Assembly);
        services.Replace(ServiceDescriptor.Singleton(new InvoiceDraftReviewFixturePricing(pricing)));
    }
}
