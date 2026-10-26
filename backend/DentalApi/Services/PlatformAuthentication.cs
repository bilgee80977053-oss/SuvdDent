using System.Security.Claims;
using System.Text;
using DentalApi.Data;
using DentalApi.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace DentalApi.Services;

public sealed class PlatformJwtService(IConfiguration config)
{
    public const string Scheme = "Platform";
    public const string Policy = "PlatformAccount";
    public (string Key, string Issuer, string Audience) Settings()
    {
        var key = config["PlatformJwt:Key"];
        var issuer = config["PlatformJwt:Issuer"];
        var audience = config["PlatformJwt:Audience"];
        if (string.IsNullOrWhiteSpace(key) || Encoding.UTF8.GetByteCount(key) < 32 ||
            string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(audience) ||
            key == config["Jwt:Key"] || issuer == config["Jwt:Issuer"] || audience == config["Jwt:Audience"])
            throw new InvalidOperationException("Separate PlatformJwt configuration is required.");
        return (key, issuer, audience);
    }
    public string Create(PlatformAccount account)
    {
        var settings = Settings();
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[] {
                new Claim("sub", account.Id.ToString()), new Claim("accountId", account.Id.ToString()),
                new Claim("identityType", "platform"), new Claim("tokenVersion", account.TokenVersion.ToString()) }),
            Issuer = settings.Issuer, Audience = settings.Audience,
            Expires = DateTime.UtcNow.AddMinutes(30), TokenType = "nexora-platform+jwt",
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Key)), SecurityAlgorithms.HmacSha256)
        });
    }
}

public static class PlatformAuthentication
{
    public static void AddPlatformIdentity(this IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<PlatformJwtService>();
        services.AddScoped<PlatformAccountService>();
        // No SMTP/SMS transport exists here. Production must explicitly configure one.
        services.AddSingleton<IPlatformMessageSender, DisabledPlatformMessageSender>();
        services.AddAuthentication().AddJwtBearer(PlatformJwtService.Scheme, options =>
        {
            var settings = new PlatformJwtService(config).Settings();
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ValidateIssuerSigningKey = true,
                ValidIssuer = settings.Issuer, ValidAudience = settings.Audience,
                ValidTypes = ["nexora-platform+jwt"], ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Key)),
                ClockSkew = TimeSpan.FromSeconds(30), NameClaimType = "sub"
            };
            options.Events = new JwtBearerEvents { OnTokenValidated = async context =>
            {
                var p = context.Principal!;
                if (!int.TryParse(p.FindFirst("accountId")?.Value, out var id) || id <= 0 ||
                    p.FindFirst("sub")?.Value != id.ToString() || p.FindFirst("identityType")?.Value != "platform" ||
                    p.HasClaim(c => c.Type is "clinicId" or "userId" or "role") ||
                    !int.TryParse(p.FindFirst("tokenVersion")?.Value, out var version) || version < 0)
                { context.Fail("Invalid platform identity."); return; }
                var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                if (!await db.PlatformAccounts.AsNoTracking().AnyAsync(a => a.Id == id && a.IsActive &&
                    (a.IsEmailVerified || a.IsPhoneVerified) && a.TokenVersion == version, context.HttpContext.RequestAborted))
                    context.Fail("Platform token revoked.");
            }};
        });
        services.AddAuthorization(o => o.AddPolicy(PlatformJwtService.Policy, p => p
            .AddAuthenticationSchemes(PlatformJwtService.Scheme).RequireAuthenticatedUser().RequireClaim("identityType", "platform")));
    }
}

public interface IPlatformMessageSender
{
    bool IsAvailable { get; }
    Task SendAsync(string channel, string destination, string purpose, string code);
}
public sealed class DisabledPlatformMessageSender : IPlatformMessageSender
{
    public bool IsAvailable => false;
    public Task SendAsync(string channel, string destination, string purpose, string code) =>
        throw new InvalidOperationException("Platform message delivery is not configured.");
}
