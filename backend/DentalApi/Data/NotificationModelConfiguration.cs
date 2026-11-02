using DentalApi.Models;
using Microsoft.EntityFrameworkCore;

namespace DentalApi.Data;

internal static class NotificationModelConfiguration
{
    internal static void Configure(ModelBuilder b)
    {
        var preference = b.Entity<NotificationPreference>();
        preference.HasKey(x => new { x.ClinicId, x.UserId });
        preference.HasOne<User>().WithMany().HasForeignKey(x => new { x.ClinicId, x.UserId })
            .HasPrincipalKey(x => new { x.ClinicId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        var row = b.Entity<NotificationOutbox>();
        row.HasOne(x => x.Appointment).WithMany().HasForeignKey(x => new { x.ClinicId, x.AppointmentId })
            .HasPrincipalKey(x => new { x.ClinicId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        row.Property(x => x.Event).HasConversion<string>().HasMaxLength(20);
        row.Property(x => x.Channel).HasConversion<string>().HasMaxLength(20);
        row.Property(x => x.State).HasConversion<string>().HasMaxLength(20);
        row.Property(x => x.Recipient).HasMaxLength(320);
        row.Property(x => x.Body).HasMaxLength(2000);
        row.HasIndex(x => new { x.ClinicId, x.AppointmentId, x.Event, x.Channel }).IsUnique();
        row.HasIndex(x => new { x.State, x.NextAttemptUtcTicks });
        row.ToTable(t =>
        {
            t.HasCheckConstraint("CK_NotificationOutbox_Channel", "\"Channel\" IN ('Email','Sms')");
            t.HasCheckConstraint("CK_NotificationOutbox_Event", "\"Event\" IN ('Confirmed','Cancelled','Reminder')");
            t.HasCheckConstraint("CK_NotificationOutbox_State", "\"State\" IN ('Pending','Sent','Suppressed','Failed')");
            t.HasCheckConstraint("CK_NotificationOutbox_Attempts", "\"Attempts\" BETWEEN 0 AND 8");
            t.HasCheckConstraint("CK_NotificationOutbox_Reminder", "\"Event\" <> 'Reminder' OR \"Channel\" = 'Sms'");
        });
    }
}
