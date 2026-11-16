using DentalApi.Data;
using DentalApi.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DentalApi.Controllers;

/// <summary>
/// Patient-owned service price estimate. Not an invoice or payment transaction.
/// Does not claim a balance, payment status, tax amount, or paid receipt.
/// This endpoint deliberately performs no writes or payment provider calls.
/// </summary>
[ApiController]
[Authorize(Roles = "Patient")]
[Route("api/appointments/{appointmentId:int}/payment-estimate")]
public sealed class PaymentEstimatesController(AppDbContext db) : ControllerBase
{
    public sealed record EstimateResponse(
        int AppointmentId,
        int ServiceId,
        string ServiceName,
        long? MinPriceMnt,
        long? MaxPriceMnt,
        string Currency,
        string PriceSource,
        bool IsFinalAmount,
        long? TotalDueMnt,
        string PaymentState,
        string PriceExplanation,
        string ReceiptState);

    [HttpGet]
    public async Task<IActionResult> Get(int appointmentId, CancellationToken cancellationToken)
    {
        if (appointmentId <= 0)
            return BadRequest(new { message = "Захиалгын дугаар буруу байна." });

        if (!int.TryParse(User.FindFirst("clinicId")?.Value, out var clinicId) ||
            !int.TryParse(User.FindFirst("userId")?.Value, out var userId) ||
            clinicId <= 0 || userId <= 0)
            return Unauthorized();

        // Guard ownership with verified JWT claims AND the clinic/patient relationship.
        // No cross-tenant or cross-patient existence leak on a missing estimate.
        var appointment = await db.Appointments.AsNoTracking()
            .Where(a => a.Id == appointmentId && a.ClinicId == clinicId &&
                db.Patients.Any(p => p.Id == a.PatientId &&
                    p.ClinicId == clinicId && p.UserId == userId && p.IsActive))
            .Select(a => new { a.Id, a.ServiceId })
            .SingleOrDefaultAsync(cancellationToken);
        if (appointment is null) return NotFound();

        var service = await db.Services.AsNoTracking()
            .Where(s => s.Id == appointment.ServiceId && s.ClinicId == clinicId)
            .Select(s => new { s.Id, s.Name, s.MinPrice, s.MaxPrice })
            .SingleOrDefaultAsync(cancellationToken);
        if (service is null) return NotFound();

        var min = service.MinPrice > 0 ? (long?)service.MinPrice : null;
        var max = service.MaxPrice >= service.MinPrice && service.MaxPrice > 0
            ? service.MaxPrice
            : min;

        return Ok(new EstimateResponse(
            appointment.Id, service.Id, service.Name,
            min, max, "MNT", "ClinicServiceCatalog",
            IsFinalAmount: false,
            TotalDueMnt: null,
            PaymentState: "NotTracked",
            PriceExplanation: "Энэ нь эмнэлгийн үйлчилгээний бүртгэлд байгаа урьдчилсан үнэ. " +
                "Оношилгоо, материал, нэмэлт ажилбар, хөнгөлөлт, татварын нөхцөл болон " +
                "эцсийн дүнг эмнэлэг баталгаажуулаагүй. Үйлчилгээний тариф өөрчлөгдөж болно.",
            ReceiptState: "NotIssued"));
    }
}
