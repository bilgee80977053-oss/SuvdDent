using DentalApi.Models;
using Microsoft.EntityFrameworkCore;

namespace DentalApi.Data;

internal static class AppointmentModelConfiguration
{
    internal static void Configure(ModelBuilder model, bool sqlite)
    {
        var appointment = model.Entity<Appointment>();
        appointment.HasOne<Clinic>().WithMany().HasForeignKey(a => a.ClinicId)
            .OnDelete(DeleteBehavior.Restrict);
        appointment.HasOne<Patient>().WithMany()
            .HasForeignKey(a => new { a.ClinicId, a.PatientId })
            .HasPrincipalKey(p => new { p.ClinicId, p.Id }).OnDelete(DeleteBehavior.Restrict);
        appointment.HasOne<DoctorProfile>().WithMany()
            .HasForeignKey(a => new { a.ClinicId, a.DoctorProfileId })
            .HasPrincipalKey(d => new { d.ClinicId, d.Id }).OnDelete(DeleteBehavior.Restrict);
        appointment.HasOne<Service>().WithMany()
            .HasForeignKey(a => new { a.ClinicId, a.ServiceId })
            .HasPrincipalKey(s => new { s.ClinicId, s.Id }).OnDelete(DeleteBehavior.Restrict);
        appointment.Property(a => a.Status).HasConversion<string>().HasMaxLength(20)
            .HasDefaultValue(AppointmentStatus.Confirmed);
        appointment.Property(a => a.ClinicTimeZoneIdSnapshot).HasMaxLength(100);
        appointment.HasIndex(a => new { a.ClinicId, a.PatientId, a.RequestId }).IsUnique();
        appointment.HasIndex(a => new { a.ClinicId, a.PatientId, a.StartUtc });
        // A lookup index, not an exclusion constraint. It does not prevent overlaps.
        appointment.HasIndex(a => new { a.ClinicId, a.DoctorProfileId, a.Status, a.OccupiedStartUtc, a.OccupiedEndUtc });

        foreach (var name in new[] { nameof(Appointment.StartUtc), nameof(Appointment.EndUtc),
            nameof(Appointment.OccupiedStartUtc), nameof(Appointment.OccupiedEndUtc), nameof(Appointment.CreatedAtUtc) })
        {
            var time = appointment.Property<DateTimeOffset>(name);
            if (sqlite)
                time.HasConversion(x => x.UtcDateTime.Ticks, x => new DateTimeOffset(x, TimeSpan.Zero));
            else
                time.HasConversion(x => x.ToUniversalTime(), x => x.ToUniversalTime())
                    .HasColumnType("timestamp with time zone");
        }

        appointment.ToTable(table =>
        {
            table.HasCheckConstraint("CK_Appointments_Interval",
                "\"OccupiedStartUtc\" <= \"StartUtc\" AND \"StartUtc\" < \"EndUtc\" AND \"EndUtc\" <= \"OccupiedEndUtc\"");
            table.HasCheckConstraint("CK_Appointments_SnapshotBounds",
                "\"DurationMinutesSnapshot\" BETWEEN 1 AND 1440 AND \"BufferBeforeMinutesSnapshot\" BETWEEN 0 AND 1439 AND " +
                "\"BufferAfterMinutesSnapshot\" BETWEEN 0 AND 1439 AND CAST(\"DurationMinutesSnapshot\" AS bigint) + \"BufferBeforeMinutesSnapshot\" + \"BufferAfterMinutesSnapshot\" <= 1440");
            table.HasCheckConstraint("CK_Appointments_SnapshotIntervals", sqlite
                ? "\"EndUtc\" - \"StartUtc\" = \"DurationMinutesSnapshot\" * 600000000 AND " +
                  "\"StartUtc\" - \"OccupiedStartUtc\" = \"BufferBeforeMinutesSnapshot\" * 600000000 AND " +
                  "\"OccupiedEndUtc\" - \"EndUtc\" = \"BufferAfterMinutesSnapshot\" * 600000000"
                : "\"EndUtc\" = \"StartUtc\" + \"DurationMinutesSnapshot\" * INTERVAL '1 minute' AND " +
                  "\"OccupiedStartUtc\" = \"StartUtc\" - \"BufferBeforeMinutesSnapshot\" * INTERVAL '1 minute' AND " +
                  "\"OccupiedEndUtc\" = \"EndUtc\" + \"BufferAfterMinutesSnapshot\" * INTERVAL '1 minute'");
            table.HasCheckConstraint("CK_Appointments_UtcRange", sqlite
                ? "\"OccupiedStartUtc\" >= 0 AND \"OccupiedEndUtc\" <= 3155378975999999999 AND \"CreatedAtUtc\" BETWEEN 0 AND 3155378975999999999"
                : "isfinite(\"OccupiedStartUtc\") AND isfinite(\"StartUtc\") AND isfinite(\"EndUtc\") AND isfinite(\"OccupiedEndUtc\") AND isfinite(\"CreatedAtUtc\")");
            table.HasCheckConstraint("CK_Appointments_Status", "\"Status\" IN ('Confirmed', 'Cancelled')");
            table.HasCheckConstraint("CK_Appointments_RequestId", "\"RequestId\" <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_Appointments_TimeZoneSnapshot", "length(trim(\"ClinicTimeZoneIdSnapshot\")) BETWEEN 1 AND 100");
        });
    }
}
