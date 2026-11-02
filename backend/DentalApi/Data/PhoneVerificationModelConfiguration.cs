using DentalApi.Models;
using Microsoft.EntityFrameworkCore;

namespace DentalApi.Data;

internal static class PhoneVerificationModelConfiguration
{
    internal static void Configure(ModelBuilder b)
    {
        var challenge = b.Entity<PhoneVerificationChallenge>();
        challenge.HasOne<Clinic>().WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Restrict);
        challenge.HasOne<User>().WithMany().HasForeignKey(x => new { x.ClinicId, x.UserId })
            .HasPrincipalKey(x => new { x.ClinicId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        challenge.Property(x => x.PhoneSnapshot).HasMaxLength(32);
        challenge.Property(x => x.Destination).HasMaxLength(12);
        challenge.Property(x => x.CodeHash).HasMaxLength(100);
        challenge.Property(x => x.DeliveryState).HasConversion<string>().HasMaxLength(20);
        challenge.HasIndex(x => new { x.ClinicId, x.UserId, x.CreatedUtcTicks });
        challenge.HasIndex(x => new { x.ClinicId, x.UserId, x.LastFailedUtcTicks });
        challenge.HasIndex(x => new { x.ClinicId, x.UserId }).IsUnique()
            .HasFilter("NOT \"IsConsumed\" AND NOT \"IsInvalidated\"");
        challenge.ToTable(t =>
        {
            t.HasCheckConstraint("CK_PhoneVerificationChallenge_Attempts", "\"FailedAttempts\" BETWEEN 0 AND 5");
            t.HasCheckConstraint("CK_PhoneVerificationChallenge_Expiry",
                "\"CreatedUtcTicks\" >= 0 AND \"ExpiresUtcTicks\" - \"CreatedUtcTicks\" = 1200000000");
            t.HasCheckConstraint("CK_PhoneVerificationChallenge_Delivery",
                "\"DeliveryState\" IN ('Pending','Accepted','Failed')");
            t.HasCheckConstraint("CK_PhoneVerificationChallenge_Consumed",
                "NOT \"IsConsumed\" OR \"DeliveryState\" = 'Accepted'");
        });
    }
}
