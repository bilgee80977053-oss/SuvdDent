using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DentalApi.Migrations
{
    /// <inheritdoc />
    public partial class AddTreatmentRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TreatmentRecords",
                columns: table => new
                {
                    ClinicId = table.Column<int>(type: "INTEGER", nullable: false),
                    AppointmentId = table.Column<int>(type: "INTEGER", nullable: false),
                    AuthorUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    SignedUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TreatmentRecords", x => new { x.ClinicId, x.AppointmentId });
                    table.CheckConstraint("CK_TreatmentRecords_NoteLength", "length(trim(\"Note\")) BETWEEN 1 AND 2000 AND typeof(\"Note\") = 'text' AND instr(\"Note\", char(0)) = 0");
                    table.CheckConstraint("CK_TreatmentRecords_SignedUtc", "typeof(\"SignedUtc\") = 'integer' AND \"SignedUtc\" BETWEEN 1 AND 3155378975999999998");
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
                CREATE TRIGGER "TR_TreatmentRecords_RequireAssignedSigner"
                BEFORE INSERT ON "TreatmentRecords"
                FOR EACH ROW WHEN NOT EXISTS (
                        SELECT 1 FROM "Appointments" a
                        JOIN "DoctorProfiles" d ON d."ClinicId" = a."ClinicId" AND d."Id" = a."DoctorProfileId"
                        JOIN "Users" u ON u."ClinicId" = d."ClinicId" AND u."Id" = d."UserId"
                        JOIN "Patients" p ON p."ClinicId" = a."ClinicId" AND p."Id" = a."PatientId"
                        WHERE a."ClinicId" = NEW."ClinicId" AND a."Id" = NEW."AppointmentId"
                          AND a."Status" = 'Confirmed' AND a."StartUtc" <= NEW."SignedUtc"
                          AND d."IsActive" AND p."IsActive" AND u."IsActive"
                          AND u."Role" = 'Doctor' AND u."Id" = NEW."AuthorUserId"
                )
                BEGIN
                    SELECT RAISE(ABORT, 'Treatment signer is not assigned to this started encounter');
                END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER "TR_TreatmentRecords_NoUpdate"
                BEFORE UPDATE ON "TreatmentRecords"
                FOR EACH ROW BEGIN
                    SELECT RAISE(ABORT, 'Signed treatment records are append-only');
                END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER "TR_TreatmentRecords_NoDelete"
                BEFORE DELETE ON "TreatmentRecords"
                FOR EACH ROW BEGIN
                    SELECT RAISE(ABORT, 'Signed treatment records are append-only');
                END;
                """);
            // Catch implicit replacement regardless of SQLite's recursive trigger setting.
            migrationBuilder.Sql("""
                CREATE TRIGGER "TR_TreatmentRecords_NoReplace"
                BEFORE INSERT ON "TreatmentRecords"
                FOR EACH ROW WHEN EXISTS (
                    SELECT 1 FROM "TreatmentRecords" WHERE "ClinicId" = NEW."ClinicId" AND "AppointmentId" = NEW."AppointmentId"
                )
                BEGIN
                    SELECT RAISE(ABORT, 'Signed treatment records are append-only');
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TreatmentRecords");
        }
    }
}
