using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace DentalApi.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequestPhoneVerificationRequest;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfirmPhoneVerificationRequest(
    [Required] Guid? ChallengeId,
    [Required, RegularExpression("^[0-9]{6}$")] string? Code);

public sealed record PhoneVerificationStatusResponse(
    string? MaskedPhone, bool IsPhoneVerified, bool PhoneEligible, bool DeliveryAvailable);

public sealed record PhoneVerificationRequestedResponse(
    Guid ChallengeId, string MaskedPhone, DateTimeOffset ExpiresAtUtc, DateTimeOffset ResendAtUtc);
