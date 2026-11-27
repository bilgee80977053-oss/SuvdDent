using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DentalApi.Migrations
{
    /// <inheritdoc />
    public partial class EnforceAppointmentIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TEMP TABLE "__AppointmentIntegrityPreflight" ("Marker" INTEGER NOT NULL);
                CREATE TEMP TRIGGER "__AppointmentIntegrityPreflight_Guard"
                BEFORE INSERT ON "__AppointmentIntegrityPreflight"
                WHEN EXISTS (
                    SELECT 1
                    FROM "Appointments" AS appointment
                    LEFT JOIN "DoctorServices" AS association
                      ON association."ClinicId" = appointment."ClinicId"
                     AND association."DoctorProfileId" = appointment."DoctorProfileId"
                     AND association."ServiceId" = appointment."ServiceId"
                    WHERE association."Id" IS NULL
                ) OR EXISTS (
                    SELECT 1 FROM "Appointments" AS leftAppointment
                    JOIN "Appointments" AS rightAppointment
                      ON rightAppointment."Id" > leftAppointment."Id"
                     AND rightAppointment."ClinicId" = leftAppointment."ClinicId"
                     AND rightAppointment."DoctorProfileId" = leftAppointment."DoctorProfileId"
                    WHERE leftAppointment."Status" = 'Confirmed'
                      AND rightAppointment."Status" = 'Confirmed'
                      AND leftAppointment."OccupiedStartUtc" < rightAppointment."OccupiedEndUtc"
                      AND rightAppointment."OccupiedStartUtc" < leftAppointment."OccupiedEndUtc"
                ) OR EXISTS (
                    SELECT 1 FROM "Appointments" AS leftAppointment
                    JOIN "Appointments" AS rightAppointment
                      ON rightAppointment."Id" > leftAppointment."Id"
                     AND rightAppointment."ClinicId" = leftAppointment."ClinicId"
                     AND rightAppointment."PatientId" = leftAppointment."PatientId"
                    WHERE leftAppointment."Status" = 'Confirmed'
                      AND rightAppointment."Status" = 'Confirmed'
                      AND leftAppointment."StartUtc" < rightAppointment."EndUtc"
                      AND rightAppointment."StartUtc" < leftAppointment."EndUtc"
                )
                BEGIN
                    SELECT RAISE(ABORT, 'Existing appointments violate doctor-service or overlap integrity.');
                END;
                INSERT INTO "__AppointmentIntegrityPreflight" VALUES (1);
                DROP TRIGGER "__AppointmentIntegrityPreflight_Guard";
                DROP TABLE "__AppointmentIntegrityPreflight";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_ClinicId_DoctorProfileId_ServiceId",
                table: "Appointments",
                columns: new[] { "ClinicId", "DoctorProfileId", "ServiceId" });

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_DoctorServices_ClinicId_DoctorProfileId_ServiceId",
                table: "Appointments",
                columns: new[] { "ClinicId", "DoctorProfileId", "ServiceId" },
                principalTable: "DoctorServices",
                principalColumns: new[] { "ClinicId", "DoctorProfileId", "ServiceId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS "TR_Appointments_NoPatientTreatmentOverlap_Update";
                DROP TRIGGER IF EXISTS "TR_Appointments_NoPatientTreatmentOverlap_Insert";
                DROP TRIGGER IF EXISTS "TR_Appointments_NoDoctorOccupiedOverlap_Update";
                DROP TRIGGER IF EXISTS "TR_Appointments_NoDoctorOccupiedOverlap_Insert";
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_DoctorServices_ClinicId_DoctorProfileId_ServiceId",
                table: "Appointments");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_ClinicId_DoctorProfileId_ServiceId",
                table: "Appointments");
        }
    }
}
