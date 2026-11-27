using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DentalApi.Migrations
{
    /// <inheritdoc />
    public partial class ProtectAppointmentOverlap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TRIGGER "TR_Appointments_NoDoctorOccupiedOverlap_Insert"
                BEFORE INSERT ON "Appointments"
                WHEN NEW."Status" = 'Confirmed' AND EXISTS (
                    SELECT 1 FROM "Appointments" AS existing
                    WHERE existing."Status" = 'Confirmed'
                      AND existing."ClinicId" = NEW."ClinicId"
                      AND existing."DoctorProfileId" = NEW."DoctorProfileId"
                      AND existing."OccupiedStartUtc" < NEW."OccupiedEndUtc"
                      AND NEW."OccupiedStartUtc" < existing."OccupiedEndUtc"
                )
                BEGIN
                    SELECT RAISE(ABORT, 'Appointment doctor occupied interval overlaps.');
                END;

                CREATE TRIGGER "TR_Appointments_NoDoctorOccupiedOverlap_Update"
                BEFORE UPDATE OF "ClinicId", "DoctorProfileId", "OccupiedStartUtc", "OccupiedEndUtc", "Status"
                ON "Appointments"
                WHEN NEW."Status" = 'Confirmed' AND EXISTS (
                    SELECT 1 FROM "Appointments" AS existing
                    WHERE existing."Id" <> NEW."Id"
                      AND existing."Status" = 'Confirmed'
                      AND existing."ClinicId" = NEW."ClinicId"
                      AND existing."DoctorProfileId" = NEW."DoctorProfileId"
                      AND existing."OccupiedStartUtc" < NEW."OccupiedEndUtc"
                      AND NEW."OccupiedStartUtc" < existing."OccupiedEndUtc"
                )
                BEGIN
                    SELECT RAISE(ABORT, 'Appointment doctor occupied interval overlaps.');
                END;

                CREATE TRIGGER "TR_Appointments_NoPatientTreatmentOverlap_Insert"
                BEFORE INSERT ON "Appointments"
                WHEN NEW."Status" = 'Confirmed' AND EXISTS (
                    SELECT 1 FROM "Appointments" AS existing
                    WHERE existing."Status" = 'Confirmed'
                      AND existing."ClinicId" = NEW."ClinicId"
                      AND existing."PatientId" = NEW."PatientId"
                      AND existing."StartUtc" < NEW."EndUtc"
                      AND NEW."StartUtc" < existing."EndUtc"
                )
                BEGIN
                    SELECT RAISE(ABORT, 'Appointment patient treatment interval overlaps.');
                END;

                CREATE TRIGGER "TR_Appointments_NoPatientTreatmentOverlap_Update"
                BEFORE UPDATE OF "ClinicId", "PatientId", "StartUtc", "EndUtc", "Status"
                ON "Appointments"
                WHEN NEW."Status" = 'Confirmed' AND EXISTS (
                    SELECT 1 FROM "Appointments" AS existing
                    WHERE existing."Id" <> NEW."Id"
                      AND existing."Status" = 'Confirmed'
                      AND existing."ClinicId" = NEW."ClinicId"
                      AND existing."PatientId" = NEW."PatientId"
                      AND existing."StartUtc" < NEW."EndUtc"
                      AND NEW."StartUtc" < existing."EndUtc"
                )
                BEGIN
                    SELECT RAISE(ABORT, 'Appointment patient treatment interval overlaps.');
                END;
                """);
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
        }
    }
}
