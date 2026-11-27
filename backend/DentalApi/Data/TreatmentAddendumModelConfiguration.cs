using DentalApi.Models;
using Microsoft.EntityFrameworkCore;

namespace DentalApi.Data;

internal static class TreatmentAddendumModelConfiguration
{
    internal static void Configure(ModelBuilder model, bool sqlite)
    {
        var addendum = model.Entity<TreatmentAddendum>();
        addendum.HasKey(x => new { x.ClinicId, x.AppointmentId, x.RequestId });
        addendum.HasOne<TreatmentRecord>().WithMany().HasForeignKey(x => new { x.ClinicId, x.AppointmentId })
            .OnDelete(DeleteBehavior.Restrict);
        addendum.HasOne<User>().WithMany().HasForeignKey(x => new { x.ClinicId, x.AuthorUserId })
            .HasPrincipalKey(x => new { x.ClinicId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        addendum.Property(x => x.Note).HasMaxLength(2000);
        var instant = addendum.Property(x => x.SignedUtc);
        if (sqlite)
            instant.HasConversion(x => x.UtcDateTime.Ticks, x => new DateTimeOffset(x, TimeSpan.Zero));
        else
            instant.HasConversion(x => x.ToUniversalTime(), x => x.ToUniversalTime())
                .HasColumnType("timestamp with time zone");
        addendum.ToTable("TreatmentAddenda", table =>
        {
            const string noteLength = """length(trim("Note")) BETWEEN 1 AND 2000""";
            table.HasCheckConstraint("CK_TreatmentAddenda_NoteLength", sqlite
                ? noteLength + """ AND typeof("Note") = 'text' AND instr("Note", char(0)) = 0"""
                : noteLength);
            const string nonemptyRequest = "\"RequestId\" <> '00000000-0000-0000-0000-000000000000'";
            table.HasCheckConstraint("CK_TreatmentAddenda_RequestId", sqlite
                ? nonemptyRequest + """
                     AND typeof("RequestId") = 'text' AND length(CAST("RequestId" AS BLOB)) = 36
                     AND upper("RequestId") = "RequestId" COLLATE BINARY
                     AND substr("RequestId", 9, 1) = '-' AND substr("RequestId", 14, 1) = '-'
                     AND substr("RequestId", 19, 1) = '-' AND substr("RequestId", 24, 1) = '-'
                     AND length(replace("RequestId", '-', '')) = 32
                     AND replace("RequestId", '-', '') NOT GLOB '*[^0-9A-F]*'
                    """
                : nonemptyRequest);
            table.HasCheckConstraint("CK_TreatmentAddenda_SignedUtc", sqlite
                ? """typeof("SignedUtc") = 'integer' AND "SignedUtc" BETWEEN 1 AND 3155378975999999998"""
                : """isfinite("SignedUtc")""");
            table.HasTrigger("TR_TreatmentAddenda_RequireOriginalSigner");
            if (sqlite)
            {
                table.HasTrigger("TR_TreatmentAddenda_NoUpdate");
                table.HasTrigger("TR_TreatmentAddenda_NoDelete");
                table.HasTrigger("TR_TreatmentAddenda_NoReplace");
            }
            else
            {
                table.HasTrigger("TR_TreatmentAddenda_NoMutation");
                table.HasTrigger("TR_TreatmentAddenda_NoTruncate");
            }
        });
    }
}
