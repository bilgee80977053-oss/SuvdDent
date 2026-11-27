using DentalApi.Models;
using Microsoft.EntityFrameworkCore;

namespace DentalApi.Data;

internal static class ClinicalAuditModelConfiguration
{
    internal static void Configure(ModelBuilder model, bool sqlite)
    {
        var audit = model.Entity<ClinicalAuditEvent>();
        audit.HasOne<Appointment>().WithMany().HasForeignKey(x => new { x.ClinicId, x.AppointmentId })
            .HasPrincipalKey(x => new { x.ClinicId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        audit.HasOne<User>().WithMany().HasForeignKey(x => new { x.ClinicId, x.ActorUserId })
            .HasPrincipalKey(x => new { x.ClinicId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        audit.Property(x => x.Action).HasConversion<string>().HasMaxLength(24);
        var instant = audit.Property(x => x.OccurredUtc);
        if (sqlite)
            instant.HasConversion(x => x.UtcDateTime.Ticks, x => new DateTimeOffset(x, TimeSpan.Zero));
        else
            instant.HasConversion(x => x.ToUniversalTime(), x => x.ToUniversalTime())
                .HasColumnType("timestamp with time zone");
        audit.HasIndex(x => new { x.ClinicId, x.AppointmentId, x.OccurredUtc });
        audit.ToTable("ClinicalAuditEvents", table =>
        {
            table.HasCheckConstraint("CK_ClinicalAuditEvents_Action",
                "\"Action\" IN ('HistoryRead','TreatmentRecorded','TreatmentAmended')");
            const string nonemptyCorrelation = "\"CorrelationId\" <> '00000000-0000-0000-0000-000000000000'";
            table.HasCheckConstraint("CK_ClinicalAuditEvents_CorrelationId", sqlite
                ? nonemptyCorrelation + """
                     AND typeof("CorrelationId") = 'text' AND length(CAST("CorrelationId" AS BLOB)) = 36
                     AND substr("CorrelationId", 9, 1) = '-' AND substr("CorrelationId", 14, 1) = '-'
                     AND substr("CorrelationId", 19, 1) = '-' AND substr("CorrelationId", 24, 1) = '-'
                     AND length(replace("CorrelationId", '-', '')) = 32
                     AND replace("CorrelationId", '-', '') NOT GLOB '*[^0-9A-Fa-f]*'
                    """
                : nonemptyCorrelation);
            table.HasCheckConstraint("CK_ClinicalAuditEvents_OccurredUtc", sqlite
                ? "typeof(\"OccurredUtc\") = 'integer' AND \"OccurredUtc\" BETWEEN 1 AND 3155378975999999998"
                : "isfinite(\"OccurredUtc\")");
            if (sqlite)
            {
                table.HasTrigger("TR_ClinicalAuditEvents_NoUpdate");
                table.HasTrigger("TR_ClinicalAuditEvents_NoDelete");
                table.HasTrigger("TR_ClinicalAuditEvents_NoReplace");
            }
            else
            {
                table.HasTrigger("TR_ClinicalAuditEvents_NoMutation");
                table.HasTrigger("TR_ClinicalAuditEvents_NoTruncate");
            }
        });
    }
}
