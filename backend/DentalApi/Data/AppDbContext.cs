using DentalApi.Models;
using Microsoft.EntityFrameworkCore;

namespace DentalApi.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Clinic> Clinics => Set<Clinic>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Service> Services => Set<Service>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>().HasIndex(u => new { u.ClinicId, u.Phone }).IsUnique();
        b.Entity<User>().Property(u => u.Role).HasConversion<string>();
    }
}