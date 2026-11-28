using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DentalApi.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class AddPostgreSqlTreatmentRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TreatmentRecords",
                columns: table => new
                {
                    ClinicId = table.Column<int>(type: "integer", nullable: false),
                    AppointmentId = table.Column<int>(type: "integer", nullable: false),
                    AuthorUserId = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    SignedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TreatmentRecords", x => new { x.ClinicId, x.AppointmentId });
                    table.CheckConstraint("CK_TreatmentRecords_NoteLength", "length(trim(\"Note\")) BETWEEN 1 AND 2000");
                    table.CheckConstraint("CK_TreatmentRecords_SignedUtc", "isfinite(\"SignedUtc\")");
                    table.ForeignKey(
                        name: "FK_TreatmentRecords_Appointments_ClinicId_AppointmentId",
                        columns: x => new { x.ClinicId, x.AppointmentId },
                        principalTable: "Appointments",
                        principalColumns: new[] { "ClinicId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TreatmentRecords_Users_ClinicId_AuthorUserId",
                        columns: x => new { x.ClinicId, x.AuthorUserId },
                        principalTable: "Users",
                        principalColumns: new[] { "ClinicId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentRecords_ClinicId_AuthorUserId",
                table: "TreatmentRecords",
                columns: new[] { "ClinicId", "AuthorUserId" });

            migrationBuilder.Sql("""
                CREATE FUNCTION "GuardSignedTreatmentRecord"() RETURNS trigger
                LANGUAGE plpgsql
                AS $treatment_guard$
                BEGIN
                    IF TG_OP = 'INSERT' THEN
                        IF NOT EXISTS (
                        SELECT 1 FROM "Appointments" a
                        JOIN "DoctorProfiles" d ON d."ClinicId" = a."ClinicId" AND d."Id" = a."DoctorProfileId"
                        JOIN "Users" u ON u."ClinicId" = d."ClinicId" AND u."Id" = d."UserId"
                        JOIN "Patients" p ON p."ClinicId" = a."ClinicId" AND p."Id" = a."PatientId"
                        WHERE a."ClinicId" = NEW."ClinicId" AND a."Id" = NEW."AppointmentId"
                          AND a."Status" = 'Confirmed' AND a."StartUtc" <= NEW."SignedUtc"
                          AND d."IsActive" AND p."IsActive" AND u."IsActive"
                          AND u."Role" = 'Doctor' AND u."Id" = NEW."AuthorUserId"
                        ) THEN
                            RAISE EXCEPTION USING ERRCODE = '23514', MESSAGE = 'Treatment signer is not assigned to this started encounter';
                        END IF;
                        RETURN NEW;
                    END IF;
                    RAISE EXCEPTION USING ERRCODE = '55000', MESSAGE = 'Signed treatment records are append-only';
                END;
                $treatment_guard$;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER "TR_TreatmentRecords_RequireAssignedSigner"
                BEFORE INSERT ON "TreatmentRecords" FOR EACH ROW
                EXECUTE FUNCTION "GuardSignedTreatmentRecord"();
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER "TR_TreatmentRecords_NoMutation"
                BEFORE UPDATE OR DELETE ON "TreatmentRecords" FOR EACH ROW
                EXECUTE FUNCTION "GuardSignedTreatmentRecord"();
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER "TR_TreatmentRecords_NoTruncate"
                BEFORE TRUNCATE ON "TreatmentRecords" FOR EACH STATEMENT
                EXECUTE FUNCTION "GuardSignedTreatmentRecord"();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TreatmentRecords");
            migrationBuilder.Sql("""DROP FUNCTION "GuardSignedTreatmentRecord"();""");
        }
    }
}
