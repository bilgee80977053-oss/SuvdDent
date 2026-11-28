using DentalApi.Data;
using DentalApi.Models;
using DentalApi.Services;
using Microsoft.AspNetCore.RateLimiting;
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
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/appointments/{appointmentId:int}/payment-estimate")]
public sealed class PaymentEstimatesController(AppDbContext db, EmailService email, IConfiguration configuration) : ControllerBase
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
    /// <summary>
    /// Explicit user-initiated email of an estimate, never a paid receipt.
    /// Recipient is read from verified same-clinic JWT identity (not request input).
    /// </summary>
    [HttpPost("email")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> SendEstimateEmail(
        int appointmentId, CancellationToken cancellationToken)
    {
        var result = await Get(appointmentId, cancellationToken);
        if (result is not OkObjectResult { Value: EstimateResponse estimate })
            return result;

        if (!int.TryParse(User.FindFirst("clinicId")?.Value, out var clinicId) ||
            !int.TryParse(User.FindFirst("userId")?.Value, out var userId))
            return Unauthorized();

        var recipient = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId && u.ClinicId == clinicId &&
                u.IsActive && u.Role == Role.Patient && u.IsEmailVerified &&
                u.Email != null && u.Email.Trim() != "")
            .Select(u => u.Email)
            .SingleOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(recipient))
            return BadRequest(new { message = "Баталгаажсан имэйл хаяг шаардлагатай." });

        if (new[] { "Email:Host", "Email:Port", "Email:From", "Email:Username", "Email:Password" }
            .Any(key => string.IsNullOrWhiteSpace(configuration[key])))
            return StatusCode(503, new { message = "Эмнэлгийн имэйл илгээх тохиргоо бүрэн биш." });

        string price = estimate.MinPriceMnt is null
            ? "Үнэ тодруулаагүй"
            : estimate.MaxPriceMnt is not null && estimate.MaxPriceMnt != estimate.MinPriceMnt
                ? $"{estimate.MinPriceMnt:N0}–{estimate.MaxPriceMnt:N0} ₮"
                : $"{estimate.MinPriceMnt:N0} ₮";
        var body = "SUV DENT — ҮНИЙН МЭДЭЭЛЛИЙН ТӨСӨЛ\n" +
            "Энэ нь төлбөр төлсөн баримт, татварын eBarimt биш.\n\n" +
            $"Захиалгын дугаар: {estimate.AppointmentId}\n" +
            $"Үйлчилгээ: {estimate.ServiceName}\n" +
            $"Бүртгэлийн үнэ: {price}\n" +
            "Нэмэлт ажилбар, материал, хөнгөлөлт, татварын нөхцөлийг эмнэлэг баталгаажуулна.\n" +
            "Эцсийн төлөх дүн одоогоор баталгаажаагүй.\n\n" +
            "Дархан-Уул • Suvd Dent\n";

        try
        {
            await email.SendAsync(recipient,
                "Suvd Dent — Үнийн мэдээллийн төсөл", body,
                cancellationToken: cancellationToken);
            return Ok(new { message = "Үнийн мэдээллийн төсөл баталгаажсан имэйл рүү илгээгдлээ." });
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Do not leak SMTP credentials, addresses or message details.
            return StatusCode(503,
                new { message = "Имэйл илгээлт баталгаажаагүй. Дахин шалгана уу." });
        }
    }

}
