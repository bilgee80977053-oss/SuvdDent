using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DentalApi.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class AddPostgreSqlTreatmentAddenda : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TreatmentAddenda",
                columns: table => new
                {
                    ClinicId = table.Column<int>(type: "integer", nullable: false),
                    AppointmentId = table.Column<int>(type: "integer", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorUserId = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    SignedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TreatmentAddenda", x => new { x.ClinicId, x.AppointmentId, x.RequestId });
                    table.CheckConstraint("CK_TreatmentAddenda_NoteLength", "length(trim(\"Note\")) BETWEEN 1 AND 2000");
                    table.CheckConstraint("CK_TreatmentAddenda_RequestId", "\"RequestId\" <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_TreatmentAddenda_SignedUtc", "isfinite(\"SignedUtc\")");
                    table.ForeignKey(
                        name: "FK_TreatmentAddenda_TreatmentRecords_ClinicId_AppointmentId",
                        columns: x => new { x.ClinicId, x.AppointmentId },
                        principalTable: "TreatmentRecords",
                        principalColumns: new[] { "ClinicId", "AppointmentId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TreatmentAddenda_Users_ClinicId_AuthorUserId",
                        columns: x => new { x.ClinicId, x.AuthorUserId },
                        principalTable: "Users",
                        principalColumns: new[] { "ClinicId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentAddenda_ClinicId_AuthorUserId",
                table: "TreatmentAddenda",
                columns: new[] { "ClinicId", "AuthorUserId" });

            migrationBuilder.Sql("""
                CREATE FUNCTION "GuardSignedTreatmentAddendum"() RETURNS trigger
                LANGUAGE plpgsql
                AS $treatment_addendum_guard$
                BEGIN
                    IF TG_OP = 'INSERT' THEN
                        IF NOT EXISTS (
                        SELECT 1 FROM "TreatmentRecords" r
                        JOIN "Appointments" a ON a."ClinicId" = r."ClinicId" AND a."Id" = r."AppointmentId"
                        JOIN "DoctorProfiles" d ON d."ClinicId" = a."ClinicId" AND d."Id" = a."DoctorProfileId"
                        JOIN "Users" u ON u."ClinicId" = d."ClinicId" AND u."Id" = d."UserId"
                        JOIN "Patients" p ON p."ClinicId" = a."ClinicId" AND p."Id" = a."PatientId"
                        WHERE r."ClinicId" = NEW."ClinicId" AND r."AppointmentId" = NEW."AppointmentId"
                          AND r."AuthorUserId" = NEW."AuthorUserId" AND r."SignedUtc" <= NEW."SignedUtc"
                          AND a."Status" = 'Confirmed' AND a."StartUtc" <= NEW."SignedUtc"
                          AND d."IsActive" AND p."IsActive" AND u."IsActive"
                          AND u."Role" = 'Doctor' AND u."Id" = NEW."AuthorUserId"
                        ) THEN
                            RAISE EXCEPTION USING ERRCODE = '23514', MESSAGE = 'Only the active assigned original signer may add an addendum';
                        END IF;
                        RETURN NEW;
                    END IF;
                    RAISE EXCEPTION USING ERRCODE = '55000', MESSAGE = 'Signed treatment addenda are append-only';
                END;
                $treatment_addendum_guard$;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER "TR_TreatmentAddenda_RequireOriginalSigner"
                BEFORE INSERT ON "TreatmentAddenda" FOR EACH ROW
                EXECUTE FUNCTION "GuardSignedTreatmentAddendum"();
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER "TR_TreatmentAddenda_NoMutation"
                BEFORE UPDATE OR DELETE ON "TreatmentAddenda" FOR EACH ROW
                EXECUTE FUNCTION "GuardSignedTreatmentAddendum"();
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER "TR_TreatmentAddenda_NoTruncate"
                BEFORE TRUNCATE ON "TreatmentAddenda" FOR EACH STATEMENT
                EXECUTE FUNCTION "GuardSignedTreatmentAddendum"();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TreatmentAddenda");
            migrationBuilder.Sql("""DROP FUNCTION "GuardSignedTreatmentAddendum"();""");
        }
    }
}
