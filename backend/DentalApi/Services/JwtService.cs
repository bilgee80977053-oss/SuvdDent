
using System.Security.Claims;
using System.Text;
using DentalApi.Models;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace DentalApi.Services;

public class JwtService(IConfiguration cfg)
{
    public string Create(User user)
    {
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(cfg["Jwt:Key"]!)
        );

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim("sub", user.Id.ToString()),
                new Claim("userId", user.Id.ToString()),
                new Claim("name", user.FullName),
                new Claim("role", user.Role.ToString()),
                new Claim("clinicId", user.ClinicId.ToString()),
                new Claim("tokenVersion", user.TokenVersion.ToString())
            }),

            Issuer = cfg["Jwt:Issuer"],
            Audience = cfg["Jwt:Audience"],

            Expires = DateTime.UtcNow.AddHours(
                double.Parse(cfg["Jwt:ExpireHours"]!)
            ),

            SigningCredentials = new SigningCredentials(
                key,
                SecurityAlgorithms.HmacSha256
            )
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
