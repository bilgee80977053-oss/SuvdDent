namespace DentalApi.Contracts;

public sealed record PlatformClinicDto(int Id, string Name, string TimeZoneId);
public sealed record PlatformDoctorDto(int Id, string FullName, string Specialty,
    int PracticeStartYear, string? Biography, string? PhotoPath);
public sealed record PlatformDoctorServiceDto(int Id, string Name, string Description,
    long MinPrice, long? MaxPrice, int DurationMinutes, int BufferBeforeMinutes, int BufferAfterMinutes);
public sealed record PlatformCatalogError(string ErrorCode);
