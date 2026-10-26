using DentalApi.Contracts;
using DentalApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DentalApi.Controllers;

[ApiController, Authorize(Roles = "Director")]
[Route("api/admin/Doctors/{doctorId:int}/services")]
public class DoctorServicesController(SchedulingService scheduling) : ControllerBase
{
    private int ClinicId => int.Parse(User.FindFirst("clinicId")!.Value);
    [HttpGet]
    public async Task<IActionResult> Get(int doctorId) => Respond(await scheduling.ReadServices(ClinicId, doctorId));
    [HttpPut]
    public async Task<IActionResult> Put(int doctorId, UpdateDoctorServicesRequest request) =>
        Respond(await scheduling.WriteServices(ClinicId, doctorId, request));
    private IActionResult Respond(SchedulingResult result) => StatusCode(result.Status, result.Data);
}

[ApiController, Authorize(Roles = "Patient")]
[Route("api/Patients/me/availability")]
public class PatientAvailabilityController(SchedulingService scheduling) : ControllerBase
{
    private int ClinicId => int.Parse(User.FindFirst("clinicId")!.Value);
    private int UserId => int.Parse(User.FindFirst("userId")!.Value);
    [HttpGet]
    public async Task<IActionResult> Get() => Respond(await scheduling.ReadAvailability(ClinicId, UserId));
    [HttpPut]
    public async Task<IActionResult> Put(UpdateAvailabilityRequest request) =>
        Respond(await scheduling.WriteAvailability(ClinicId, UserId, request));
    private IActionResult Respond(SchedulingResult result) => StatusCode(result.Status, result.Data);
}
