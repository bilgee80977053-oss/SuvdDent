using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DentalApi.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class EnforceAppointmentIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $preflight$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM public."Appointments" AS appointment
                        LEFT JOIN public."DoctorServices" AS association
                          ON association."ClinicId" = appointment."ClinicId"
                         AND association."DoctorProfileId" = appointment."DoctorProfileId"
                         AND association."ServiceId" = appointment."ServiceId"
                        WHERE association."Id" IS NULL
                    ) THEN
                        RAISE EXCEPTION 'Existing appointments reference no matching doctor-service association.';
                    END IF;

                    IF EXISTS (
                        SELECT 1 FROM public."Appointments" AS leftAppointment
                        JOIN public."Appointments" AS rightAppointment
                          ON rightAppointment."Id" > leftAppointment."Id"
                         AND rightAppointment."ClinicId" = leftAppointment."ClinicId"
                         AND rightAppointment."DoctorProfileId" = leftAppointment."DoctorProfileId"
                        WHERE leftAppointment."Status" = 'Confirmed'
                          AND rightAppointment."Status" = 'Confirmed'
                          AND tstzrange(leftAppointment."OccupiedStartUtc", leftAppointment."OccupiedEndUtc", '[)')
                              && tstzrange(rightAppointment."OccupiedStartUtc", rightAppointment."OccupiedEndUtc", '[)')
                    ) THEN
                        RAISE EXCEPTION 'Existing confirmed appointments overlap a doctor occupied interval.';
                    END IF;

                    IF EXISTS (
                        SELECT 1 FROM public."Appointments" AS leftAppointment
                        JOIN public."Appointments" AS rightAppointment
                          ON rightAppointment."Id" > leftAppointment."Id"
                         AND rightAppointment."ClinicId" = leftAppointment."ClinicId"
                         AND rightAppointment."PatientId" = leftAppointment."PatientId"
                        WHERE leftAppointment."Status" = 'Confirmed'
                          AND rightAppointment."Status" = 'Confirmed'
                          AND tstzrange(leftAppointment."StartUtc", leftAppointment."EndUtc", '[)')
                              && tstzrange(rightAppointment."StartUtc", rightAppointment."EndUtc", '[)')
                    ) THEN
                        RAISE EXCEPTION 'Existing confirmed appointments overlap a patient treatment interval.';
                    END IF;
                END;
                $preflight$;
                """);

            migrationBuilder.DropIndex(
                name: "IX_DoctorServices_ClinicId_DoctorProfileId_ServiceId",
                table: "DoctorServices");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:CollationDefinition:platform_email_nocase", "und-u-ks-level2,und-u-ks-level2,icu,False")
                .Annotation("Npgsql:PostgresExtension:btree_gist", ",,")
                .OldAnnotation("Npgsql:CollationDefinition:platform_email_nocase", "und-u-ks-level2,und-u-ks-level2,icu,False");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_DoctorServices_ClinicId_DoctorProfileId_ServiceId",
                table: "DoctorServices",
                columns: new[] { "ClinicId", "DoctorProfileId", "ServiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_ClinicId_DoctorProfileId_ServiceId",
                table: "Appointments",
                columns: new[] { "ClinicId", "DoctorProfileId", "ServiceId" });

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_DoctorServices_ClinicId_DoctorProfileId_Servic~",
                table: "Appointments",
                columns: new[] { "ClinicId", "DoctorProfileId", "ServiceId" },
                principalTable: "DoctorServices",
                principalColumns: new[] { "ClinicId", "DoctorProfileId", "ServiceId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                ALTER TABLE public."Appointments"
                ADD CONSTRAINT "EX_Appointments_ConfirmedDoctorOccupied_NoOverlap"
                EXCLUDE USING gist (
                    "ClinicId" WITH =,
                    "DoctorProfileId" WITH =,
                    tstzrange("OccupiedStartUtc", "OccupiedEndUtc", '[)') WITH &&
                )
                WHERE ("Status" = 'Confirmed');

                ALTER TABLE public."Appointments"
                ADD CONSTRAINT "EX_Appointments_ConfirmedPatientTreatment_NoOverlap"
                EXCLUDE USING gist (
                    "ClinicId" WITH =,
                    "PatientId" WITH =,
                    tstzrange("StartUtc", "EndUtc", '[)') WITH &&
                )
                WHERE ("Status" = 'Confirmed');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE public."Appointments"
                    DROP CONSTRAINT "EX_Appointments_ConfirmedPatientTreatment_NoOverlap";
                ALTER TABLE public."Appointments"
                    DROP CONSTRAINT "EX_Appointments_ConfirmedDoctorOccupied_NoOverlap";
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_DoctorServices_ClinicId_DoctorProfileId_Servic~",
                table: "Appointments");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_DoctorServices_ClinicId_DoctorProfileId_ServiceId",
                table: "DoctorServices");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_ClinicId_DoctorProfileId_ServiceId",
                table: "Appointments");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:CollationDefinition:platform_email_nocase", "und-u-ks-level2,und-u-ks-level2,icu,False")
                .OldAnnotation("Npgsql:CollationDefinition:platform_email_nocase", "und-u-ks-level2,und-u-ks-level2,icu,False");

            migrationBuilder.CreateIndex(
                name: "IX_DoctorServices_ClinicId_DoctorProfileId_ServiceId",
                table: "DoctorServices",
                columns: new[] { "ClinicId", "DoctorProfileId", "ServiceId" },
                unique: true);
        }
    }
}
