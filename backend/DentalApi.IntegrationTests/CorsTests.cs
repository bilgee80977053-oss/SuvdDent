using Xunit;

namespace DentalApi.IntegrationTests;

public sealed class CorsTests
{
    [Theory]
    [InlineData("https://allowed.example.invalid", true)]
    [InlineData("https://other.example.invalid", false)]
    public async Task PreflightAllowsOnlyConfiguredOrigin(string origin, bool allowed)
    {
        using var factory = new DentalApiFactory();
        using var client = factory.CreateTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/Patients/me");
        request.Headers.TryAddWithoutValidation("Origin", origin);
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "GET");

        using var response = await client.SendAsync(request);
        var actual = response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values)
            ? values.SingleOrDefault() : null;
        Assert.Equal(allowed ? origin : null, actual);
    }
}
