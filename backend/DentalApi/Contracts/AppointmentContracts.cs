using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using DentalApi.Models;

namespace DentalApi.Contracts;

public record BookAppointmentRequest([Range(1, int.MaxValue)] int DoctorProfileId,
    [Range(1, int.MaxValue)] int ServiceId,
    [property: JsonRequired, JsonConverter(typeof(RequiredOffsetConverter))] DateTimeOffset StartUtc,
    Guid ClinicVersion, Guid DoctorVersion, bool ConsentToCreateNewPatient = false);
public record AppointmentResponse(int Id, int ClinicId, int DoctorProfileId, int ServiceId,
    AppointmentStatus Status, DateTimeOffset StartUtc, DateTimeOffset EndUtc,
    string ClinicTimeZoneId, string DoctorName, string ServiceName,
    int DurationMinutes, int BufferBeforeMinutes, int BufferAfterMinutes,
    long MinPrice, long? MaxPrice, DateTime CreatedAtUtc);
public record BookingResult(int Status, AppointmentResponse? Appointment = null, string? ErrorCode = null);

public record BookingOptionsResponse(int ClinicId, int DoctorProfileId, int ServiceId,
    Guid ClinicVersion, Guid DoctorVersion, string ClinicTimeZoneId,
    int DurationMinutes, int BufferBeforeMinutes, int BufferAfterMinutes,
    WorkingHoursInput[] WorkingHours);
