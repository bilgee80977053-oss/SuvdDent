using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace DentalApi.Contracts;

public record DoctorServiceInput([Range(1, int.MaxValue)] int ServiceId,
    [Range(1, 1440)] int DurationMinutes, [Range(0, 1439)] int BufferBeforeMinutes,
    [Range(0, 1439)] int BufferAfterMinutes, bool IsActive = true);
public record UpdateDoctorServicesRequest(Guid Version,
    [Required, MaxLength(100)] DoctorServiceInput[] Services);
public record DoctorServicesResponse(int DoctorProfileId, Guid Version, DoctorServiceInput[] Services);
public record AvailabilityInput(
    [property: JsonRequired, JsonConverter(typeof(RequiredOffsetConverter))] DateTimeOffset StartUtc,
    [property: JsonRequired, JsonConverter(typeof(RequiredOffsetConverter))] DateTimeOffset EndUtc);
public record UpdateAvailabilityRequest(Guid Version,
    [Required, MaxLength(100)] AvailabilityInput[] Intervals);
public record AvailabilityResponse(Guid Version, AvailabilityInput[] Intervals);

// DateTimeOffset's default JSON converter accepts missing offsets, which is unsafe here.
public sealed class RequiredOffsetConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String) throw new JsonException("An ISO timestamp with offset is required.");
        var value = reader.GetString()!;
        if (!Regex.IsMatch(value, @"\A\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:[.]\d{1,7})?(?:Z|[+-]\d{2}:\d{2})\z") ||
            !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant))
            throw new JsonException("An ISO timestamp with explicit offset is required.");
        return instant.ToUniversalTime();
    }
    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToUniversalTime());
}
