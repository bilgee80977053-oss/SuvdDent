using DentalApi.Contracts;
using DentalApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DentalApi.Controllers;

[ApiController]
[Authorize(Roles = "Director")]
[Route("api/admin/Doctors")]
public class AdminDoctorsController(DoctorProfileService profiles) : ControllerBase
{
    private int ClinicId => int.Parse(User.FindFirst("clinicId")!.Value);

    [HttpGet]
    public async Task<IActionResult> List() => Ok(await profiles.ListAsync(ClinicId, true));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var profile = await profiles.ReadAsync(ClinicId, id, null, false);
        return profile is null ? NotFound() : Ok(profile);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateDoctorRequest request)
    {
        var result = await profiles.CreateAsync(ClinicId, request);
        if (result.Status == 201 && result.Profile is not null)
            return CreatedAtAction(nameof(Get), new { id = result.Profile.Id }, result.Profile);
        return Respond(result);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, ManageDoctorRequest request) =>
        Respond(await profiles.UpdateAsync(ClinicId, id, null, request.Version, request.Profile,
            request.IsPublished, request.IsActive));

    [HttpPut("{id:int}/working-hours")]
    public async Task<IActionResult> UpdateHours(int id, UpdateWorkingHoursRequest request) =>
        Respond(await profiles.UpdateAsync(ClinicId, id, null, request.Version, null, hours: request.Intervals));

    private IActionResult Respond(DoctorWriteResult result) =>
        StatusCode(result.Status, (object?)result.Profile ?? new { error = result.Error });
}