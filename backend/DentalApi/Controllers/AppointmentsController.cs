using DentalApi.Contracts;
using DentalApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DentalApi.Controllers;

[ApiController, Authorize(Policy = PlatformJwtService.Policy)]
[Route("api/platform")]
public class PlatformAppointmentsController(AppointmentService appointments) : ControllerBase
{
    private int AccountId => int.Parse(User.FindFirst("accountId")!.Value);
    [HttpGet("clinics/{clinicId:int}/booking-options")]
    public async Task<IActionResult> Options(int clinicId, int doctorProfileId, int serviceId)
    {
        var result = await appointments.Options(clinicId, doctorProfileId, serviceId);
        return StatusCode(result.Status, result.Data);
    }
    [HttpPost("clinics/{clinicId:int}/appointments")]
    public async Task<IActionResult> Book(int clinicId, BookAppointmentRequest request, [FromHeader(Name = "Idempotency-Key")] string? key)
    {
        if (!Guid.TryParse(key, out var parsed) || parsed == Guid.Empty) return BadRequest(new { errorCode = "IDEMPOTENCY_KEY_REQUIRED" });
        var result = await appointments.Book(AccountId, int.Parse(User.FindFirst("tokenVersion")!.Value), clinicId, parsed, request);
        if (result.Status == 201) return CreatedAtAction(nameof(Get), new { id = result.Appointment!.Id }, result.Appointment);
        return StatusCode(result.Status, (object?)result.Appointment ?? new { errorCode = result.ErrorCode });
    }
    [HttpGet("me/appointments")]
    public async Task<IActionResult> List([FromQuery] int skip = 0, [FromQuery] int take = 50) => skip < 0 || take is < 1 or > 100
        ? BadRequest() : Ok(await appointments.Mine(AccountId, skip, take));
    [HttpGet("me/appointments/{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var value = await appointments.Own(AccountId, id);
        return value is null ? NotFound() : Ok(value);
    }
}

[ApiController, Authorize(Roles = "Director,Doctor,Nurse")]
[Route("api/appointments")]
public class StaffAppointmentsController(AppointmentService appointments) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int skip = 0, [FromQuery] int take = 50) => skip < 0 || take is < 1 or > 100
        ? BadRequest() : Ok(await appointments.Staff(int.Parse(User.FindFirst("clinicId")!.Value),
            int.Parse(User.FindFirst("userId")!.Value), User.IsInRole("Doctor"), skip, take));
}
