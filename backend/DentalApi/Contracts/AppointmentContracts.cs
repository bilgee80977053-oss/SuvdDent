using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace DentalApi.Contracts;

// String input requires an explicit timezone offset; offset-free JSON is never guessed.
public record CreateAppointmentRequest(int DoctorProfileId, int ServiceId,
    [Required] string StartUtc, Guid RequestId);
public record RescheduleAppointmentRequest([Required] string StartUtc, Guid RequestId);
public record AppointmentResponse(int Id, int DoctorProfileId, int ServiceId,
    DateTimeOffset StartUtc, DateTimeOffset EndUtc, DateTimeOffset OccupiedStartUtc,
    DateTimeOffset OccupiedEndUtc, string ClinicTimeZoneId, string Status, DateTimeOffset CreatedAtUtc);
public sealed record AppointmentHistoryResponse : AppointmentResponse
{
    public AppointmentHistoryResponse(AppointmentResponse response, string? doctorName, string? serviceName)
        : base(response)
    {
        DoctorName = doctorName;
        ServiceName = serviceName;
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DoctorName { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ServiceName { get; init; }
}
public record AppointmentHistoryPage(int Page, int PageSize, AppointmentHistoryResponse[] Items);
public record AppointmentPage(int Page, int PageSize, AppointmentResponse[] Items);
public record AppointmentSlot(DateTimeOffset StartUtc, DateTimeOffset EndUtc);
public record AppointmentSlotsResponse(string Date, string TimeZoneId, AppointmentSlot[] Slots);
public record BookingDoctorResponse(int Id, string FullName, string Specialty,
    int PracticeStartYear, string? Biography, string? PhotoPath);
public record BookingServiceResponse(int Id, string Name, string Description, string? ImagePath,
    long MinPrice, long? MaxPrice, string? AftercareText = null);
