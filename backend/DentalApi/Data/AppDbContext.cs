
using DentalApi.Models;
using Microsoft.EntityFrameworkCore;

namespace DentalApi.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected AppDbContext(DbContextOptions options) : base(options) { }
    public DbSet<Clinic> Clinics => Set<Clinic>();
    public DbSet<Appointment> Appointments => Set<Appointment>();

    public DbSet<User> Users => Set<User>();

    public DbSet<Patient> Patients => Set<Patient>();

    public DbSet<Service> Services => Set<Service>();

    public DbSet<VerificationCode> VerificationCodes =>
        Set<VerificationCode>();

    public DbSet<DoctorProfile> DoctorProfiles => Set<DoctorProfile>();
    public DbSet<DoctorWorkingHours> DoctorWorkingHours => Set<DoctorWorkingHours>();

    public DbSet<DoctorService> DoctorServices => Set<DoctorService>();
    public DbSet<PatientAvailabilitySchedule> PatientAvailabilitySchedules => Set<PatientAvailabilitySchedule>();
    public DbSet<PatientAvailability> PatientAvailabilities => Set<PatientAvailability>();

    public DbSet<PlatformAccount> PlatformAccounts => Set<PlatformAccount>();
    public DbSet<PlatformVerificationCode> PlatformVerificationCodes => Set<PlatformVerificationCode>();
    public DbSet<ClinicPatientLink> ClinicPatientLinks => Set<ClinicPatientLink>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        AppointmentModelConfiguration.Configure(b, Database.IsSqlite());

        // Preserve the TokenVersion default introduced by its SQLite migration
        // when rebuilding Users for its tenant alternate key.
        b.Entity<User>().Property(u => u.TokenVersion).HasDefaultValue(0);

        // Хэрэглэгчийн утас давхардахгүй
        b.Entity<User>()
            .HasIndex(u => new { u.ClinicId, u.Phone })
            .IsUnique()
            .HasFilter("\"Phone\" IS NOT NULL");

        // Хэрэглэгчийн имэйл давхардахгүй
        b.Entity<User>()
            .HasIndex(u => new { u.ClinicId, u.Email })
            .IsUnique()
            .HasFilter("\"Email\" IS NOT NULL");

        // Хэрэглэгчийн эрхийг текстээр хадгалах
        b.Entity<User>()
            .Property(u => u.Role)
            .HasConversion<string>();

        // Баталгаажуулах кодын холбоо
        b.Entity<VerificationCode>()
            .HasOne(v => v.User)
            .WithMany()
            .HasForeignKey(v => v.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Өвчтөн ба хэрэглэгчийн холбоо
        b.Entity<Patient>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(p => new { p.ClinicId, p.UserId })
            .HasPrincipalKey(u => new { u.ClinicId, u.Id })
            .OnDelete(DeleteBehavior.Restrict);

        // Нэг эмнэлэгт нэг хэрэглэгчтэй
        // давхар өвчтөний бүртгэл үүсгэхгүй
        b.Entity<Patient>().HasIndex(p => p.UserId);

        b.Entity<Patient>()
            .HasIndex(p => new { p.ClinicId, p.UserId })
            .IsUnique()
            .HasFilter("\"UserId\" IS NOT NULL");

        b.Entity<DoctorProfile>()
            .HasOne(d => d.User).WithMany()
            .HasForeignKey(d => new { d.ClinicId, d.UserId })
            .HasPrincipalKey(u => new { u.ClinicId, u.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.Entity<DoctorProfile>().HasIndex(d => new { d.ClinicId, d.UserId }).IsUnique();
        b.Entity<DoctorProfile>().HasOne<Clinic>().WithMany()
            .HasForeignKey(d => d.ClinicId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<DoctorProfile>().Property(d => d.Version).IsConcurrencyToken();
        b.Entity<DoctorProfile>().Property(d => d.Specialty).HasMaxLength(200);
        b.Entity<DoctorProfile>().Property(d => d.Biography).HasMaxLength(4000);
        b.Entity<DoctorProfile>().Property(d => d.PhotoPath).HasMaxLength(300);

        b.Entity<DoctorWorkingHours>()
            .HasOne<DoctorProfile>().WithMany(d => d.WorkingHours)
            .HasForeignKey(h => new { h.ClinicId, h.DoctorProfileId })
            .HasPrincipalKey(d => new { d.ClinicId, d.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.Entity<DoctorWorkingHours>()
            .HasIndex(h => new { h.ClinicId, h.DoctorProfileId, h.DayOfWeek, h.StartMinute }).IsUnique();
        b.Entity<DoctorWorkingHours>().ToTable(t => t.HasCheckConstraint(
            "CK_DoctorWorkingHours_Interval",
            "\"DayOfWeek\" BETWEEN 0 AND 6 AND \"StartMinute\" >= 0 AND \"StartMinute\" < \"EndMinute\" AND \"EndMinute\" <= 1440"));
        b.Entity<Clinic>().Property(c => c.TimeZoneId).HasMaxLength(100)
            .HasDefaultValue("Asia/Ulaanbaatar");
        b.Entity<DoctorService>().HasOne<DoctorProfile>().WithMany()
            .HasForeignKey(x => new { x.ClinicId, x.DoctorProfileId })
            .HasPrincipalKey(x => new { x.ClinicId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<DoctorService>().HasOne<Service>().WithMany()
            .HasForeignKey(x => new { x.ClinicId, x.ServiceId })
            .HasPrincipalKey(x => new { x.ClinicId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<DoctorService>().HasIndex(x => new { x.ClinicId, x.DoctorProfileId, x.ServiceId }).IsUnique();
        b.Entity<DoctorService>().ToTable(t => t.HasCheckConstraint("CK_DoctorServices_Duration",
            "\"DurationMinutes\" > 0 AND \"BufferBeforeMinutes\" >= 0 AND \"BufferAfterMinutes\" >= 0 AND \"DurationMinutes\" + \"BufferBeforeMinutes\" + \"BufferAfterMinutes\" <= 1440"));
        b.Entity<PatientAvailabilitySchedule>().HasKey(x => new { x.ClinicId, x.PatientId });
        b.Entity<PatientAvailabilitySchedule>().Property(x => x.Version).IsConcurrencyToken();
        b.Entity<PatientAvailabilitySchedule>().HasOne<Patient>().WithMany()
            .HasForeignKey(x => new { x.ClinicId, x.PatientId })
            .HasPrincipalKey(x => new { x.ClinicId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<PatientAvailability>().HasOne<PatientAvailabilitySchedule>().WithMany()
            .HasForeignKey(x => new { x.ClinicId, x.PatientId }).OnDelete(DeleteBehavior.Restrict);
        // SQLite compares UTC ticks numerically; offsets never affect ordering.
        b.Entity<PatientAvailability>().Property(x => x.StartUtc)
            .HasConversion(x => x.UtcDateTime.Ticks, x => new DateTimeOffset(x, TimeSpan.Zero));
        b.Entity<PatientAvailability>().Property(x => x.EndUtc)
            .HasConversion(x => x.UtcDateTime.Ticks, x => new DateTimeOffset(x, TimeSpan.Zero));
        b.Entity<PatientAvailability>().HasIndex(x => new { x.ClinicId, x.PatientId, x.StartUtc }).IsUnique();
        b.Entity<PatientAvailability>().ToTable(t => t.HasCheckConstraint("CK_PatientAvailabilities_Interval",
            "\"StartUtc\" >= 0 AND \"StartUtc\" < \"EndUtc\" AND \"EndUtc\" <= 3155378975999999999"));
        b.Entity<Clinic>().Property(c => c.Id).ValueGeneratedOnAdd();
        if (Database.IsSqlite())
            b.Entity<Clinic>().Property(c => c.Id).HasAnnotation("Sqlite:Autoincrement", true);
        b.Entity<Clinic>().Property(c => c.CountryCode).HasMaxLength(2);
        b.Entity<Clinic>().Property(c => c.ProvinceOrCapitalCode).HasMaxLength(2);
        b.Entity<Clinic>().Property(c => c.SoumOrDistrictCode).HasMaxLength(4);
        b.Entity<Clinic>().Property(c => c.AddressLine).HasMaxLength(500);
        b.Entity<Clinic>().Property(c => c.TimeZoneStatus).HasConversion<string>()
            .HasDefaultValue(TimeZoneStatus.PendingVerification);
        b.Entity<Clinic>().Property(c => c.Version).IsConcurrencyToken()
            .HasDefaultValue(Guid.Empty);
        b.Entity<Clinic>().HasOne<User>().WithMany()
            .HasForeignKey(c => new { c.TimeZoneVerifiedClinicId, c.TimeZoneVerifiedByUserId })
            .HasPrincipalKey(u => new { u.ClinicId, u.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Clinic>().ToTable(t => t.HasCheckConstraint("CK_Clinics_TimeZoneVerification",
            "(\"TimeZoneStatus\" = 'PendingVerification' AND \"TimeZoneVerifiedClinicId\" IS NULL AND \"TimeZoneVerifiedByUserId\" IS NULL AND \"TimeZoneVerifiedAtUtc\" IS NULL) OR " +
            "(\"TimeZoneStatus\" = 'Verified' AND \"TimeZoneId\" IN ('Asia/Ulaanbaatar', 'Asia/Hovd') AND " +
            "\"CountryCode\" IS NOT NULL AND \"CountryCode\" = 'MN' AND \"ProvinceOrCapitalCode\" IS NOT NULL AND " +
            "\"SoumOrDistrictCode\" IS NOT NULL AND \"AddressLine\" IS NOT NULL AND length(trim(\"AddressLine\")) > 0 AND " +
            "\"TimeZoneVerifiedClinicId\" IS NOT NULL AND \"TimeZoneVerifiedClinicId\" = \"Id\" AND \"TimeZoneVerifiedByUserId\" IS NOT NULL AND \"TimeZoneVerifiedAtUtc\" IS NOT NULL)"));
        if (Database.IsSqlite())
            b.Entity<PlatformAccount>().Property(x => x.Email).UseCollation("NOCASE");
        else if (Database.IsNpgsql())
        {
            // PostgreSQL has no SQLite NOCASE collation. ICU provides case-insensitive uniqueness.
            b.HasCollation("platform_email_nocase", locale: "und-u-ks-level2", provider: "icu", deterministic: false);
            b.Entity<PlatformAccount>().Property(x => x.Email).UseCollation("platform_email_nocase");
            // Birth dates are calendar values, not UTC instants. Preserve the DateTime API.
            b.Entity<Patient>().Property(x => x.DateOfBirth)
                .HasConversion(x => x.HasValue ? DateTime.SpecifyKind(x.Value, DateTimeKind.Unspecified) : x,
                    x => x.HasValue ? DateTime.SpecifyKind(x.Value, DateTimeKind.Unspecified) : x)
                .HasColumnType("timestamp without time zone");
        }
        b.Entity<PlatformAccount>().HasIndex(x => x.Email).IsUnique().HasFilter("\"Email\" IS NOT NULL");
        b.Entity<PlatformAccount>().HasIndex(x => x.Phone).IsUnique().HasFilter("\"Phone\" IS NOT NULL");
        b.Entity<PlatformAccount>().ToTable(t => t.HasCheckConstraint("CK_PlatformAccounts_Contacts",
            "(\"Email\" IS NOT NULL OR \"Phone\" IS NOT NULL) AND \"TokenVersion\" >= 0"));
        b.Entity<PlatformVerificationCode>().HasOne<PlatformAccount>().WithMany()
            .HasForeignKey(x => x.PlatformAccountId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<PlatformVerificationCode>().HasIndex(x => new { x.PlatformAccountId, x.Channel, x.Purpose, x.CreatedAt });
        b.Entity<ClinicPatientLink>().HasOne<PlatformAccount>().WithMany()
            .HasForeignKey(x => x.PlatformAccountId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ClinicPatientLink>().HasOne<Patient>().WithMany()
            .HasForeignKey(x => new { x.ClinicId, x.PatientId })
            .HasPrincipalKey(x => new { x.ClinicId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ClinicPatientLink>().HasIndex(x => new { x.PlatformAccountId, x.ClinicId }).IsUnique();
        b.Entity<ClinicPatientLink>().HasIndex(x => new { x.ClinicId, x.PatientId }).IsUnique();
    }
}
