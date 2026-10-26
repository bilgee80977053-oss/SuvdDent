using System.ComponentModel.DataAnnotations;
using DentalApi.Models;

namespace DentalApi.Contracts;

public record LocationSelection([Required, StringLength(2, MinimumLength = 2)] string CountryCode,
    [Required, StringLength(2, MinimumLength = 2)] string ProvinceOrCapitalCode,
    [Required, StringLength(4, MinimumLength = 4)] string SoumOrDistrictCode);
public record UpdateClinicLocationRequest(Guid Version, [Required] LocationSelection Location,
    [Required, StringLength(500)] string AddressLine);
public record ConfirmClinicTimeZoneRequest(Guid Version, [Required] string TimeZoneId);
public record ClinicLocationResponse(string? CountryCode, string? ProvinceOrCapitalCode,
    string? SoumOrDistrictCode, string? AddressLine, string CandidateTimeZoneId,
    string? SuggestedTimeZoneId, string? EffectiveTimeZoneId, TimeZoneStatus TimeZoneStatus,
    DateTime? TimeZoneVerifiedAtUtc, Guid Version);
