using DentalApi.Models;
using Microsoft.EntityFrameworkCore;

namespace DentalApi.Data;

internal static class PerformedProcedureModelConfiguration
{
    internal static void Configure(ModelBuilder model, bool sqlite)
    {
        var item = model.Entity<PerformedProcedure>();
        item.HasKey(row => new { row.ClinicId, row.AppointmentId, row.RequestId });
        item.HasOne<Appointment>().WithMany().HasForeignKey(row => new { row.ClinicId, row.AppointmentId })
            .HasPrincipalKey(row => new { row.ClinicId, row.Id }).OnDelete(DeleteBehavior.Restrict);
        item.HasOne<User>().WithMany().HasForeignKey(row => new { row.ClinicId, row.DoctorUserId })
            .HasPrincipalKey(row => new { row.ClinicId, row.Id }).OnDelete(DeleteBehavior.Restrict);
        item.HasOne<Service>().WithMany().HasForeignKey(row => new { row.ClinicId, row.ServiceId })
            .HasPrincipalKey(row => new { row.ClinicId, row.Id }).OnDelete(DeleteBehavior.Restrict);
        item.Property(row => row.ServiceLabel).HasMaxLength(160);
        item.Property(row => row.TargetScope).HasMaxLength(12);
        item.Property(row => row.ToothCode).HasMaxLength(2);
        var instant = item.Property(row => row.SignedUtc);
        if (sqlite)
            instant.HasConversion(x => x.UtcDateTime.Ticks, x => new DateTimeOffset(x, TimeSpan.Zero));
        else
            instant.HasConversion(x => x.ToUniversalTime(), x => x.ToUniversalTime())
                .HasColumnType("timestamp with time zone");

        item.ToTable("PerformedProcedures", table =>
        {
            const string nonemptyRequest = "\"RequestId\" <> '00000000-0000-0000-0000-000000000000'";
            table.HasCheckConstraint("CK_PerformedProcedures_RequestId", sqlite
                ? nonemptyRequest + """
                     AND typeof("RequestId") = 'text' AND length(CAST("RequestId" AS BLOB)) = 36
                     AND substr("RequestId", 9, 1) = '-' AND substr("RequestId", 14, 1) = '-'
                     AND substr("RequestId", 19, 1) = '-' AND substr("RequestId", 24, 1) = '-'
                     AND length(replace("RequestId", '-', '')) = 32
                     AND replace("RequestId", '-', '') NOT GLOB '*[^0-9A-F]*'
                    """
                : nonemptyRequest);
            table.HasCheckConstraint("CK_PerformedProcedures_Target", sqlite ? """
                ("TargetScope" = 'Visit' AND "ToothCode" IS NULL) OR
                ("TargetScope" = 'SingleTooth' AND "ToothCode" IS NOT NULL AND typeof("ToothCode") = 'text'
                    AND length(CAST("ToothCode" AS BLOB)) = 2
                    AND ("ToothCode" GLOB '[1-4][1-8]' OR "ToothCode" GLOB '[5-8][1-5]'))
                """ : """
                ("TargetScope" = 'Visit' AND "ToothCode" IS NULL) OR
                ("TargetScope" = 'SingleTooth' AND "ToothCode" IS NOT NULL AND
                    (("ToothCode" COLLATE "C") ~ '^([1-4][1-8]|[5-8][1-5])$'))
                """);
            table.HasCheckConstraint("CK_PerformedProcedures_Label", sqlite
                ? """typeof("ServiceLabel") = 'text' AND length(trim("ServiceLabel")) BETWEEN 2 AND 160 AND instr("ServiceLabel", char(0)) = 0"""
                : """length(trim("ServiceLabel")) BETWEEN 2 AND 160""");
            table.HasCheckConstraint("CK_PerformedProcedures_SignedUtc", sqlite
                ? """typeof("SignedUtc") = 'integer' AND "SignedUtc" BETWEEN 1 AND 3155378975999999998"""
                : """isfinite("SignedUtc")""");
            table.HasTrigger("TR_PerformedProcedures_CheckAssignment");
            if (sqlite)
            {
                table.HasTrigger("TR_PerformedProcedures_NoUpdate");
                table.HasTrigger("TR_PerformedProcedures_NoDelete");
                table.HasTrigger("TR_PerformedProcedures_NoReplace");
            }
            else
            {
                table.HasTrigger("TR_PerformedProcedures_NoMutation");
                table.HasTrigger("TR_PerformedProcedures_NoTruncate");
            }
        });
    }
}
