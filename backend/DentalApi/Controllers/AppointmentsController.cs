using DentalApi.Contracts;
using DentalApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DentalApi.Controllers;

[ApiController, Authorize(Roles = "Patient")]
[Route("api/appointments")]
public sealed class AppointmentsController(AppointmentService appointments) : ControllerBase
{
    private int ClinicId => int.Parse(User.FindFirst("clinicId")!.Value);
    private int UserId => int.Parse(User.FindFirst("userId")!.Value);
    private IActionResult Respond(AppointmentResult result) => StatusCode(result.Status, result.Data);

    [HttpGet("services")]
    public async Task<IActionResult> Services() => Respond(await appointments.Services(ClinicId));

    [HttpGet("doctors")]
    public async Task<IActionResult> Doctors([FromQuery] int serviceId) => Respond(await appointments.DoctorsForService(ClinicId, serviceId));

    [HttpGet("slots")]
    public async Task<IActionResult> Slots([FromQuery] int doctorProfileId, [FromQuery] int serviceId, [FromQuery] string? date) =>
        Respond(await appointments.Slots(ClinicId, UserId, doctorProfileId, serviceId, date));

    [HttpPost]
    public async Task<IActionResult> Create(CreateAppointmentRequest request)
    {
        var result = await appointments.Create(ClinicId, UserId, request);
        return result.Status == 201 && result.Data is AppointmentResponse appointment
            ? CreatedAtAction(nameof(Get), new { id = appointment.Id }, appointment) : Respond(result);
    }

    [HttpGet("me")]
    public async Task<IActionResult> Mine([FromQuery] int page = 1, [FromQuery] int pageSize = 20) =>
        Respond(await appointments.Read(ClinicId, UserId, null, page, pageSize));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id) => Respond(await appointments.Read(ClinicId, UserId, id));
}
