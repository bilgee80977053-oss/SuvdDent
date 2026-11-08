using System.Security.Cryptography;
using System.Text;
using DentalApi.Contracts;
using DentalApi.Data;
using DentalApi.Models;
using DentalApi.Services;
using Microsoft.EntityFrameworkCore;

namespace DentalApi.Demo;

// Not a record: no generated ToString that could reveal passwords.
public sealed class DemoCredentials(string patientPassword, string directorPassword)
{
    public string PatientPassword { get; } = patientPassword;
    public string DirectorPassword { get; } = directorPassword;
    public void Validate()
    {
        foreach (var password in new[] { PatientPassword, DirectorPassword })
            if (string.IsNullOrWhiteSpace(password) || password.Length < 12 || Encoding.UTF8.GetByteCount(password) > 72)
                throw new InvalidOperationException("Private DEMO passwords must satisfy the existing password policy.");
    }
}

public sealed class DemoDataSeeder(AppDbContext db, ClinicLocationService locations,
    AppointmentService appointments, TimeProvider clock)
{
    public async Task<DemoManifest> SeedAsync(DemoCredentials credentials, string directoryName)
    {
        credentials.Validate();
        if (await db.Clinics.AnyAsync() || await db.Users.AnyAsync() || await db.PlatformAccounts.AnyAsync() ||
            await db.Patients.AnyAsync() || await db.Services.AnyAsync())
            throw new InvalidOperationException("DEMO seeding requires an empty, newly provisioned database.");
        var now = clock.GetUtcNow();
        var clinic = new Clinic { Name = "Nexora DEMO ?????? ???????", WorkHours = "Mon-Fri 09:00-13:00, 14:00-18:00" };
        db.Clinics.Add(clinic); await db.SaveChangesAsync();
        var director = new User { ClinicId=clinic.Id, FullName="DEMO ???????", Email="director@nexora.example.invalid",
            PasswordHash=BCrypt.Net.BCrypt.HashPassword(credentials.DirectorPassword,12), Role=Role.Director, IsEmailVerified=true, CreatedAt=now.UtcDateTime };
        var doctors = new[] { "DEMO ??? ???", "DEMO ??? ????", "DEMO ??? ?????" }.Select((name,i) => new User
        {
            ClinicId=clinic.Id, FullName=name, Email=$"doctor{i+1}@nexora.example.invalid", Role=Role.Doctor,
            IsEmailVerified=true, CreatedAt=now.UtcDateTime,
            PasswordHash=BCrypt.Net.BCrypt.HashPassword(Convert.ToHexString(RandomNumberGenerator.GetBytes(24)),12)
        }).ToArray();
        var account = new PlatformAccount { FullName="DEMO ???????????", Email="patient@nexora.example.invalid",
            IsActive=true, IsEmailVerified=true, IsPhoneVerified=false, Phone=null, CreatedAt=now.UtcDateTime,
            PasswordHash=BCrypt.Net.BCrypt.HashPassword(credentials.PatientPassword,12) };
        db.Users.Add(director); db.Users.AddRange(doctors); db.PlatformAccounts.Add(account); await db.SaveChangesAsync();
        var profiles = doctors.Select(d => new DoctorProfile { ClinicId=clinic.Id, UserId=d.Id,
            Specialty="DEMO dentistry", PracticeStartYear=2020, IsPublished=true, Biography="?????? ????????? ???????? ???." }).ToArray();
        var names = new[] { "?????? ?????", "??? ??????????", "?????", "??? ?????", "??????????" };
        var prices = new long[] { 20000,80000,120000,90000,250000 };
        var services = names.Select((name,i) => new Service { ClinicId=clinic.Id, Name=name, Description="DEMO ????????? ? ????????? ?????.", MinPrice=prices[i], MaxPrice=prices[i] }).ToArray();
        db.DoctorProfiles.AddRange(profiles); db.Services.AddRange(services); await db.SaveChangesAsync();
        var durations = new[] { 20,40,45,30,60 }; var after = new[] { 5,10,10,15,10 };
        foreach (var (doctor,service) in new[] { (0,0),(0,2),(1,0),(1,1),(1,4),(2,0),(2,3) })
            db.DoctorServices.Add(new DoctorService { ClinicId=clinic.Id, DoctorProfileId=profiles[doctor].Id,
                ServiceId=services[service].Id, DurationMinutes=durations[service], BufferBeforeMinutes=5, BufferAfterMinutes=after[service] });
        foreach (var profile in profiles)
            for (var day=1; day<=5; day++)
                foreach (var (start,end) in new[] { (540,780),(840,1080) })
                    db.DoctorWorkingHours.Add(new DoctorWorkingHours { ClinicId=clinic.Id, DoctorProfileId=profile.Id, DayOfWeek=day, StartMinute=start, EndMinute=end });
        await db.SaveChangesAsync();
        var updated = await locations.Update(clinic.Id,director.Id,new(clinic.Version,new("MN","11","1101"),"DEMO ????????? ???? ? ???????? ????"));
        if (updated.Status != 200) throw new InvalidOperationException("DEMO location validation failed.");
        var location = (await locations.Read(clinic.Id))!;
        if ((await locations.Confirm(clinic.Id,director.Id,new(location.Version,"Asia/Ulaanbaatar"))).Status != 200)
            throw new InvalidOperationException("DEMO timezone confirmation failed.");
        var zone=TimeZoneInfo.FindSystemTimeZoneById("Asia/Ulaanbaatar");
        var dayToBook=TimeZoneInfo.ConvertTime(now,zone).Date.AddDays(1);
        while (dayToBook.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) dayToBook=dayToBook.AddDays(1);
        DateTimeOffset Instant(DateTime day,int hour,int minute)
        {
            var local=DateTime.SpecifyKind(day.AddHours(hour).AddMinutes(minute),DateTimeKind.Unspecified);
            return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local,zone),TimeSpan.Zero);
        }
        var optionsResult=await appointments.Options(clinic.Id,profiles[0].Id,services[0].Id);
        if (optionsResult.Status != 200) throw new InvalidOperationException("DEMO booking configuration failed.");
        var options=(BookingOptionsResponse)optionsResult.Data!;
        var startTime=Instant(dayToBook,9,10);
        var booking=await appointments.Book(account.Id,account.TokenVersion,clinic.Id,Guid.NewGuid(),
            new(profiles[0].Id,services[0].Id,startTime,options.ClinicVersion,options.DoctorVersion,true));
        if (booking.Status != 201) throw new InvalidOperationException("DEMO sample booking failed.");
        var candidates=new List<DemoSlot>();
        for (var day=0; day<3; day++)
        {
            candidates.Add(new("?????",profiles[0].Id,services[2].Id,Instant(dayToBook,10,10)));
            candidates.Add(new("??????????",profiles[1].Id,services[1].Id,Instant(dayToBook,11,10)));
            candidates.Add(new("??? ?????",profiles[2].Id,services[3].Id,Instant(dayToBook,14,10)));
            do { dayToBook=dayToBook.AddDays(1); } while(dayToBook.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);
        }
        return new(DemoDatabaseSafety.DatasetVersion,directoryName,DemoDatabaseSafety.DatabaseFile,"",now.UtcDateTime,
            clinic.Id,account.Id,profiles.Select(x=>x.Id).ToArray(),services.Select(x=>x.Id).ToArray(),booking.Appointment!.Id,startTime,candidates.ToArray());
    }
}
