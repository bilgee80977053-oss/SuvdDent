using DentalApi.Models;
using Microsoft.EntityFrameworkCore;

namespace DentalApi.Data;

internal static class TreatmentRecordModelConfiguration
{
    internal static void Configure(ModelBuilder model, bool sqlite)
    {
        var record = model.Entity<TreatmentRecord>();
        record.HasKey(x => new { x.ClinicId, x.AppointmentId });
        record.HasOne<Appointment>().WithMany().HasForeignKey(x => new { x.ClinicId, x.AppointmentId })
            .HasPrincipalKey(x => new { x.ClinicId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        record.HasOne<User>().WithMany().HasForeignKey(x => new { x.ClinicId, x.AuthorUserId })
            .HasPrincipalKey(x => new { x.ClinicId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        record.Property(x => x.Note).HasMaxLength(2000);
        var instant = record.Property(x => x.SignedUtc);
        if (sqlite)
            instant.HasConversion(x => x.UtcDateTime.Ticks, x => new DateTimeOffset(x, TimeSpan.Zero));
        else
            instant.HasConversion(x => x.ToUniversalTime(), x => x.ToUniversalTime())
                .HasColumnType("timestamp with time zone");

        record.ToTable("TreatmentRecords", table =>
        {
            const string textLength = """length(trim("Note")) BETWEEN 1 AND 2000""";
            table.HasCheckConstraint("CK_TreatmentRecords_NoteLength", sqlite
                ? textLength + """ AND typeof("Note") = 'text' AND instr("Note", char(0)) = 0"""
                : textLength);
            table.HasCheckConstraint("CK_TreatmentRecords_SignedUtc", sqlite
                ? """typeof("SignedUtc") = 'integer' AND "SignedUtc" BETWEEN 1 AND 3155378975999999998"""
                : """isfinite("SignedUtc")""");
            table.HasTrigger("TR_TreatmentRecords_RequireAssignedSigner");
            if (sqlite)
            {
                table.HasTrigger("TR_TreatmentRecords_NoUpdate");
                table.HasTrigger("TR_TreatmentRecords_NoDelete");
                table.HasTrigger("TR_TreatmentRecords_NoReplace");
            }
            else
            {
                table.HasTrigger("TR_TreatmentRecords_NoMutation");
                table.HasTrigger("TR_TreatmentRecords_NoTruncate");
            }
        });
    }
}
