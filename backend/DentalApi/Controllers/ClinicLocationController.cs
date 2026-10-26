using DentalApi.Contracts;
using DentalApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DentalApi.Controllers;

[ApiController, Authorize]
[Route("api/locations")]
public class LocationsController(MongoliaLocations locations) : ControllerBase
{
    [HttpGet]
    public IActionResult List() => Ok(new { countryCode = "MN", provinces = locations.Provinces() });
    [HttpGet("{province}/districts")]
    public IActionResult Districts(string province) => locations.Provinces().Any(p => p.Code == province)
        ? Ok(locations.Districts(province)) : NotFound();
    [HttpPost("timezone-suggestion")]
    public IActionResult Suggest(LocationSelection request)
    {
        var zone = locations.Suggest(request.CountryCode, request.ProvinceOrCapitalCode, request.SoumOrDistrictCode);
        return zone is null ? BadRequest() : Ok(new { suggestedTimeZoneId = zone, requiresDirectorConfirmation = true });
    }
}

[ApiController, Authorize(Roles = "Director")]
[Route("api/Clinic/me")]
public class ClinicLocationController(ClinicLocationService locations) : ControllerBase
{
    private int ClinicId => int.Parse(User.FindFirst("clinicId")!.Value);
    private int UserId => int.Parse(User.FindFirst("userId")!.Value);
    [HttpGet("location")]
    public async Task<IActionResult> Get()
    {
        var value = await locations.Read(ClinicId);
        return value is null ? NotFound() : Ok(value);
    }
    [HttpPut("location")]
    public async Task<IActionResult> Update(UpdateClinicLocationRequest request) =>
        Respond(await locations.Update(ClinicId, UserId, request));
    [HttpPost("timezone-confirmation")]
    public async Task<IActionResult> Confirm(ConfirmClinicTimeZoneRequest request) =>
        Respond(await locations.Confirm(ClinicId, UserId, request));
    private IActionResult Respond(SchedulingResult result) => StatusCode(result.Status, result.Data);
}
