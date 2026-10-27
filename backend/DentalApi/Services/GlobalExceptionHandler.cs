using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace DentalApi.Services;

public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        // Exception message/stack нь өвчтөний хувийн мэдээлэл агуулж болзошгүй.
        logger.LogError("Unhandled API error: {ExceptionType}, trace {TraceId}",
            exception.GetType().Name, context.TraceIdentifier);

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/problem+json";

        var problem = new ProblemDetails
        {
            Type = "https://www.rfc-editor.org/rfc/rfc9110.html#section-15.6.1",
            Title = "Серверийн дотоод алдаа",
            Status = StatusCodes.Status500InternalServerError,
            Detail = "Хүсэлтийг боловсруулахад алдаа гарлаа.",
            Instance = context.Request.Path
        };
        problem.Extensions["traceId"] = context.TraceIdentifier;

        await JsonSerializer.SerializeAsync(context.Response.Body, problem,
            new JsonSerializerOptions(JsonSerializerDefaults.Web), cancellationToken);
        return true;
    }
}
