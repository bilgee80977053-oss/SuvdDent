using DentalApi.Contracts;
using DentalApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DentalApi.Controllers;

[ApiController]
[Authorize(Roles = "Director")]
[Route("api/admin/staff")]
public sealed class DirectorStaffController(StaffInvitationService invitations) : ControllerBase
{
    private int ClinicId => int.Parse(User.FindFirst("clinicId")!.Value);

    [HttpGet("doctors")]
    public async Task<IActionResult> Doctors(CancellationToken ct) =>
        Ok(await invitations.ListAsync(ClinicId, ct));

    [HttpPost("invite-doctor")]
    [EnableRateLimiting("staff-invites")]
    public async Task<IActionResult> InviteDoctor(
        InviteDoctorRequest input, CancellationToken ct)
    {
        var result = await invitations.InviteAsync(ClinicId, input, ct);
        return StatusCode(result.Status, result.Response);
    }
}

[ApiController]
[Route("api/staff-invitations")]
public sealed class StaffInvitationAcceptanceController(
    StaffInvitationService invitations) : ControllerBase
{
    // No session exists until the invited doctor has set their password.
    [AllowAnonymous]
    [HttpPost("accept")]
    [EnableRateLimiting("staff-accept")]
    public async Task<IActionResult> Accept(
        AcceptDoctorInvitationRequest input, CancellationToken ct)
    {
        var result = await invitations.AcceptAsync(input, ct);
        return StatusCode(result.Status, result.Response);
    }
}
