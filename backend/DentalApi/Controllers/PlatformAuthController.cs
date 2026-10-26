using DentalApi.Contracts;
using DentalApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DentalApi.Controllers;

[ApiController]
[Route("api/platform/auth")]
public class PlatformAuthController(PlatformAccountService accounts) : ControllerBase
{
    [HttpPost("register"), AllowAnonymous, EnableRateLimiting("platform-auth")]
    public async Task<IActionResult> Register(PlatformRegisterRequest request) => Respond(await accounts.Register(request));
    [HttpPost("login"), AllowAnonymous, EnableRateLimiting("platform-auth")]
    public async Task<IActionResult> Login(PlatformLoginRequest request) => Respond(await accounts.Login(request));
    [HttpPost("resend-verification"), AllowAnonymous, EnableRateLimiting("platform-auth")]
    public async Task<IActionResult> Resend(PlatformIdentifierRequest request) => Respond(await accounts.Challenge(request.Identifier, false));
    [HttpPost("verify"), AllowAnonymous, EnableRateLimiting("platform-auth")]
    public async Task<IActionResult> Verify(PlatformVerifyRequest request) => Respond(await accounts.Verify(request.Identifier, request.Code));
    [HttpPost("forgot-password"), AllowAnonymous, EnableRateLimiting("platform-auth")]
    public async Task<IActionResult> Forgot(PlatformIdentifierRequest request) => Respond(await accounts.Challenge(request.Identifier, true));
    [HttpPost("reset-password"), AllowAnonymous, EnableRateLimiting("platform-auth")]
    public async Task<IActionResult> Reset(PlatformResetPasswordRequest request) => Respond(await accounts.Verify(request.Identifier, request.Code, request.NewPassword));
    [HttpGet("me"), Authorize(Policy = PlatformJwtService.Policy)]
    public async Task<IActionResult> Me()
    {
        var account = await accounts.Me(int.Parse(User.FindFirst("accountId")!.Value));
        return account is null ? Unauthorized() : Ok(account);
    }
    private IActionResult Respond(SchedulingResult result) => StatusCode(result.Status, result.Data);
}
