using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DentalApi.Migrations
{
    /// <inheritdoc />
    public partial class AddTreatmentAddenda : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TreatmentAddenda",
                columns: table => new
                {
                    ClinicId = table.Column<int>(type: "INTEGER", nullable: false),
                    AppointmentId = table.Column<int>(type: "INTEGER", nullable: false),
                    RequestId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AuthorUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    SignedUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TreatmentAddenda", x => new { x.ClinicId, x.AppointmentId, x.RequestId });
                    table.CheckConstraint("CK_TreatmentAddenda_NoteLength", "length(trim(\"Note\")) BETWEEN 1 AND 2000 AND typeof(\"Note\") = 'text' AND instr(\"Note\", char(0)) = 0");
                    table.CheckConstraint("CK_TreatmentAddenda_RequestId", "\"RequestId\" <> '00000000-0000-0000-0000-000000000000' AND typeof(\"RequestId\") = 'text' AND length(CAST(\"RequestId\" AS BLOB)) = 36\n AND upper(\"RequestId\") = \"RequestId\" COLLATE BINARY\n AND substr(\"RequestId\", 9, 1) = '-' AND substr(\"RequestId\", 14, 1) = '-'\n AND substr(\"RequestId\", 19, 1) = '-' AND substr(\"RequestId\", 24, 1) = '-'\n AND length(replace(\"RequestId\", '-', '')) = 32\n AND replace(\"RequestId\", '-', '') NOT GLOB '*[^0-9A-F]*'");
                    table.CheckConstraint("CK_TreatmentAddenda_SignedUtc", "typeof(\"SignedUtc\") = 'integer' AND \"SignedUtc\" BETWEEN 1 AND 3155378975999999998");
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
                CREATE TRIGGER "TR_TreatmentAddenda_RequireOriginalSigner"
                BEFORE INSERT ON "TreatmentAddenda"
                FOR EACH ROW WHEN NOT EXISTS (
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
                )
                BEGIN
                    SELECT RAISE(ABORT, 'Only the active assigned original signer may add an addendum');
                END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER "TR_TreatmentAddenda_NoUpdate"
                BEFORE UPDATE ON "TreatmentAddenda"
                FOR EACH ROW BEGIN
                    SELECT RAISE(ABORT, 'Signed treatment addenda are append-only');
                END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER "TR_TreatmentAddenda_NoDelete"
                BEFORE DELETE ON "TreatmentAddenda"
                FOR EACH ROW BEGIN
                    SELECT RAISE(ABORT, 'Signed treatment addenda are append-only');
                END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER "TR_TreatmentAddenda_NoReplace"
                BEFORE INSERT ON "TreatmentAddenda"
                FOR EACH ROW WHEN EXISTS (
                    SELECT 1 FROM "TreatmentAddenda"
                    WHERE "ClinicId" = NEW."ClinicId" AND "AppointmentId" = NEW."AppointmentId" AND "RequestId" = NEW."RequestId"
                )
                BEGIN
                    SELECT RAISE(ABORT, 'Signed treatment addenda are append-only');
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TreatmentAddenda");
        }
    }
}
