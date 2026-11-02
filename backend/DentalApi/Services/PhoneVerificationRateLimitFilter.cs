using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace DentalApi.Services;

public sealed class PhoneVerificationRequestLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<HttpContext> limiter = PartitionedRateLimiter.CreateChained(
        PartitionedRateLimiter.Create<HttpContext, string>(context =>
            RateLimitPartition.GetFixedWindowLimiter(
                (context.User.FindFirst("clinicId")?.Value ?? "") + ":" + (context.User.FindFirst("userId")?.Value ?? ""),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0, AutoReplenishment = true })),
        PartitionedRateLimiter.Create<HttpContext, string>(context =>
            RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0, AutoReplenishment = true })));

    public ValueTask<RateLimitLease> AcquireAsync(HttpContext context) =>
        limiter.AcquireAsync(context, 1, context.RequestAborted);
    public void Dispose() => limiter.Dispose();
}

public sealed class PhoneVerificationRateLimitFilter(PhoneVerificationRequestLimiter limiter)
    : IAsyncActionFilter, IOrderedFilter
{
    // Run before automatic model-validation errors, after authentication/authorization.
    public int Order => -3000;
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        using var lease = await limiter.AcquireAsync(context.HttpContext);
        if (!lease.IsAcquired)
        {
            context.HttpContext.Response.Headers.RetryAfter = "60";
            context.Result = new ObjectResult(new { message = "Хүсэлтийн хязгаар хүрсэн. Түр хүлээнэ үү." })
                { StatusCode = 429 };
            return;
        }
        await next();
    }
}
