namespace DentalApi.Models;

public enum Role { Patient, Doctor, Nurse, Accountant, Director }

public class Clinic
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    public string? WorkHours { get; set; }
}

public class User
{
    public int Id { get; set; }
    public int ClinicId { get; set; }
    public string FullName { get; set; } = "";
    public string Phone { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public Role Role { get; set; }
    public bool IsActive { get; set; } = true;
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