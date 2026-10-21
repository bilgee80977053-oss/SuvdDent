using DentalApi.Models;
using Microsoft.EntityFrameworkCore;

namespace DentalApi.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Clinic> Clinics => Set<Clinic>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<VerificationCode> VerificationCodes
    => Set<VerificationCode>();


protected override void OnModelCreating(ModelBuilder b)
{
    b.Entity<User>()
        .HasIndex(u => new { u.ClinicId, u.Phone })
        .IsUnique()
        .HasFilter("\"Phone\" IS NOT NULL");

    b.Entity<User>()
        .HasIndex(u => new { u.ClinicId, u.Email })
        .IsUnique()
        .HasFilter("\"Email\" IS NOT NULL");

    b.Entity<User>()
        .Property(u => u.Role)
        .HasConversion<string>();b.Entity<VerificationCode>()
    .HasOne(v => v.User)
    .WithMany()
    .HasForeignKey(v => v.UserId)
    .OnDelete(DeleteBehavior.Cascade);
}

}