
using DentalApi.Models;
using Microsoft.EntityFrameworkCore;

namespace DentalApi.Data;

public class AppDbContext(
    DbContextOptions<AppDbContext> options
) : DbContext(options)
{
    public DbSet<Clinic> Clinics => Set<Clinic>();

    public DbSet<User> Users => Set<User>();

    public DbSet<Patient> Patients => Set<Patient>();

    public DbSet<Service> Services => Set<Service>();

    public DbSet<VerificationCode> VerificationCodes =>
        Set<VerificationCode>();

    public DbSet<DoctorProfile> DoctorProfiles => Set<DoctorProfile>();
    public DbSet<DoctorWorkingHours> DoctorWorkingHours => Set<DoctorWorkingHours>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

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
    }
}
