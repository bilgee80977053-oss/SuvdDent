
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

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

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
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Нэг эмнэлэгт нэг хэрэглэгчтэй
        // давхар өвчтөний бүртгэл үүсгэхгүй
        b.Entity<Patient>()
            .HasIndex(p => new { p.ClinicId, p.UserId })
            .IsUnique()
            .HasFilter("\"UserId\" IS NOT NULL");
    }
}
