using System.ComponentModel.DataAnnotations;

namespace DentalApi.Contracts;

public sealed record UpdateNotificationPreferencesRequest(
    [Required] bool? EmailEnabled, [Required] bool? SmsEnabled);
public sealed record NotificationPreferencesResponse(
    bool EmailEnabled, bool SmsEnabled, bool IsEmailVerified, bool IsPhoneVerified);
