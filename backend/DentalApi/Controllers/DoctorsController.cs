using DentalApi.Contracts;
using DentalApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DentalApi.Controllers;

[ApiController]
[Authorize]
[Route("api/Doctors")]
public class DoctorsController(DoctorProfileService profiles) : ControllerBase
{
    private int ClinicId => int.Parse(User.FindFirst("clinicId")!.Value);
    private int UserId => int.Parse(User.FindFirst("userId")!.Value);

    [HttpGet]
    public async Task<IActionResult> List() => Ok(await profiles.ListAsync(ClinicId, false));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var profile = await profiles.ReadAsync(ClinicId, id, null, true);
        return profile is null ? NotFound() : Ok(profile);
    }

    [Authorize(Roles = "Doctor")]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var profile = await profiles.ReadAsync(ClinicId, null, UserId, false);
        return profile is null ? NotFound() : Ok(profile);
    }

    [Authorize(Roles = "Doctor")]
    [HttpPut("me")]
    public async Task<IActionResult> Update(UpdateDoctorRequest request) =>
        Respond(await profiles.UpdateAsync(ClinicId, null, UserId, request.Version, request.Profile));

    [Authorize(Roles = "Doctor")]
    [HttpPut("me/working-hours")]
    public async Task<IActionResult> UpdateHours(UpdateWorkingHoursRequest request) =>
        Respond(await profiles.UpdateAsync(ClinicId, null, UserId, request.Version, null, hours: request.Intervals));

    private IActionResult Respond(DoctorWriteResult result) =>
        StatusCode(result.Status, (object?)result.Profile ?? new { error = result.Error });
}