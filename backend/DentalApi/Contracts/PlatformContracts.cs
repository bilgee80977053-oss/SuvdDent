using System.ComponentModel.DataAnnotations;
namespace DentalApi.Contracts;

public record PlatformRegisterRequest([Required, StringLength(200)] string FullName,
    [StringLength(254)] string? Email, [StringLength(32)] string? Phone,
    [Required, StringLength(128)] string Password);
public record PlatformLoginRequest([Required, StringLength(254)] string Identifier,
    [Required, StringLength(128)] string Password);
public record PlatformIdentifierRequest([Required, StringLength(254)] string Identifier);
public record PlatformVerifyRequest([Required, StringLength(254)] string Identifier,
    [Required, RegularExpression("^[0-9]{6}$")] string Code);
public record PlatformResetPasswordRequest([Required, StringLength(254)] string Identifier,
    [Required, RegularExpression("^[0-9]{6}$")] string Code,
    [Required, StringLength(128)] string NewPassword);
public record PlatformAccountResponse(int Id, string FullName, string? Email, string? Phone,
    bool IsEmailVerified, bool IsPhoneVerified);
public record PlatformLoginResponse(string Token, PlatformAccountResponse Account);
