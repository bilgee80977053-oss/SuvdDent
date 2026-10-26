
namespace DentalApi.Models;

public enum Role { Patient, Doctor, Nurse, Accountant, Director }

public enum TimeZoneStatus { PendingVerification, Verified }

public class Clinic
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    public string? WorkHours { get; set; }
    // A legacy candidate only: scheduling must resolve through ClinicSchedulingPolicy.
    public string TimeZoneId { get; set; } = "Asia/Ulaanbaatar";
    public string? CountryCode { get; set; }
    public string? ProvinceOrCapitalCode { get; set; }
    public string? SoumOrDistrictCode { get; set; }
    public string? AddressLine { get; set; }
    public TimeZoneStatus TimeZoneStatus { get; set; } = TimeZoneStatus.PendingVerification;
    public int? TimeZoneVerifiedByUserId { get; set; }
    // Redundant tenant key keeps Clinic.Id out of a foreign key, preserving SQLite AUTOINCREMENT.
    public int? TimeZoneVerifiedClinicId { get; set; }
    public DateTime? TimeZoneVerifiedAtUtc { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}

public class User
{
    public int Id { get; set; }

    public int ClinicId { get; set; }

    public string FullName { get; set; } = "";

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public string PasswordHash { get; set; } = "";

    public Role Role { get; set; }

    public bool IsPhoneVerified { get; set; } = false;

    public bool IsEmailVerified { get; set; } = false;

    public bool IsActive { get; set; } = true;

    // JWT токеныг хүчингүй болгоход ашиглана
    public int TokenVersion { get; set; } = 0;

    public DateTime? CreatedAt { get; set; }
}

public class Service
{
    public int Id { get; set; }

    public int ClinicId { get; set; }

    public string Name { get; set; } = "";

    public string Description { get; set; } = "";

    public string? ImagePath { get; set; }

    public long MinPrice { get; set; }

    public long? MaxPrice { get; set; }

    public string? AftercareText { get; set; }

    public bool IsActive { get; set; } = true;
}
