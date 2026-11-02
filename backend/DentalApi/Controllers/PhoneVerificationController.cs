using DentalApi.Contracts;
using DentalApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace DentalApi.Controllers;

[ApiController, Authorize(Roles = "Patient")]
[Route("api/phone-verification/me")]
[ServiceFilter(typeof(PhoneVerificationRateLimitFilter), Order = -3000)]
public sealed class PhoneVerificationController(PhoneVerificationService verification) : ControllerBase
{
    private int ClinicId => int.Parse(User.FindFirst("clinicId")!.Value);
    private int UserId => int.Parse(User.FindFirst("userId")!.Value);
    private IActionResult Respond(PhoneVerificationResult result) => StatusCode(result.Status, result.Data);

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) =>
        Respond(await verification.Status(ClinicId, UserId, ct));

    [HttpPost("request")]
    public async Task<IActionResult> RequestCode(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RequestPhoneVerificationRequest? request,
        CancellationToken ct) => Respond(await verification.Request(ClinicId, UserId, ct));

    [HttpPost("confirm")]
    public async Task<IActionResult> Confirm(ConfirmPhoneVerificationRequest request, CancellationToken ct) =>
        Respond(await verification.Confirm(ClinicId, UserId, request.ChallengeId!.Value, request.Code!, ct));
}
