using System.Text.Json;
using DentalApi.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DentalApi.IntegrationTests;

public sealed class GlobalExceptionHandlerTests
{
    [Fact]
    public async Task UnhandledExceptionReturnsProblemJsonWithoutSensitiveDetails()
    {
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/Patients/me";
        context.TraceIdentifier = "synthetic-trace-id";
        context.Response.Body = new MemoryStream();

        Assert.True(await handler.TryHandleAsync(context,
            new InvalidOperationException("Sensitive-example-medical-note"), CancellationToken.None));

        Assert.Equal(500, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(500, document.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("/api/Patients/me", document.RootElement.GetProperty("instance").GetString());
        Assert.Equal("synthetic-trace-id", document.RootElement.GetProperty("traceId").GetString());

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var text = await reader.ReadToEndAsync();
        Assert.DoesNotContain("Sensitive-example-medical-note", text);
        Assert.DoesNotContain("InvalidOperationException", text);
        Assert.DoesNotContain("stackTrace", text, StringComparison.OrdinalIgnoreCase);
    }
}
