using DentalApi.Contracts;
using DentalApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DentalApi.Controllers;

[ApiController, Authorize(Roles = "Patient")]
[Route("api/notification-preferences/me")]
public sealed class NotificationPreferencesController(NotificationPreferenceService preferences) : ControllerBase
{
    private int ClinicId => int.Parse(User.FindFirst("clinicId")!.Value);
    private int UserId => int.Parse(User.FindFirst("userId")!.Value);

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var result = await preferences.Read(ClinicId, UserId, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPut]
    public async Task<IActionResult> Put(UpdateNotificationPreferencesRequest request, CancellationToken ct)
    {
        var result = await preferences.Write(ClinicId, UserId,
            request.EmailEnabled!.Value, request.SmsEnabled!.Value, ct);
        return result is null ? NotFound() : Ok(result);
    }
}
