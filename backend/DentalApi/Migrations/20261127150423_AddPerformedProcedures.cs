using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DentalApi.Migrations
{
    /// <inheritdoc />
    public partial class AddPerformedProcedures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PerformedProcedures",
                columns: table => new
                {
                    ClinicId = table.Column<int>(type: "INTEGER", nullable: false),
                    AppointmentId = table.Column<int>(type: "INTEGER", nullable: false),
                    RequestId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DoctorUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    ServiceId = table.Column<int>(type: "INTEGER", nullable: false),
                    ServiceLabel = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    TargetScope = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                    ToothCode = table.Column<string>(type: "TEXT", maxLength: 2, nullable: true),
                    SignedUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PerformedProcedures", x => new { x.ClinicId, x.AppointmentId, x.RequestId });
                    table.CheckConstraint("CK_PerformedProcedures_Label", "typeof(\"ServiceLabel\") = 'text' AND length(trim(\"ServiceLabel\")) BETWEEN 2 AND 160 AND instr(\"ServiceLabel\", char(0)) = 0");
                    table.CheckConstraint("CK_PerformedProcedures_RequestId", "\"RequestId\" <> '00000000-0000-0000-0000-000000000000' AND typeof(\"RequestId\") = 'text' AND length(CAST(\"RequestId\" AS BLOB)) = 36\n AND substr(\"RequestId\", 9, 1) = '-' AND substr(\"RequestId\", 14, 1) = '-'\n AND substr(\"RequestId\", 19, 1) = '-' AND substr(\"RequestId\", 24, 1) = '-'\n AND length(replace(\"RequestId\", '-', '')) = 32\n AND replace(\"RequestId\", '-', '') NOT GLOB '*[^0-9A-F]*'");
                    table.CheckConstraint("CK_PerformedProcedures_SignedUtc", "typeof(\"SignedUtc\") = 'integer' AND \"SignedUtc\" BETWEEN 1 AND 3155378975999999998");
                    table.CheckConstraint("CK_PerformedProcedures_Target", "(\"TargetScope\" = 'Visit' AND \"ToothCode\" IS NULL) OR\n(\"TargetScope\" = 'SingleTooth' AND \"ToothCode\" IS NOT NULL AND typeof(\"ToothCode\") = 'text'\n    AND length(CAST(\"ToothCode\" AS BLOB)) = 2\n    AND (\"ToothCode\" GLOB '[1-4][1-8]' OR \"ToothCode\" GLOB '[5-8][1-5]'))");
                    table.ForeignKey(
                        name: "FK_PerformedProcedures_Appointments_ClinicId_AppointmentId",
                        columns: x => new { x.ClinicId, x.AppointmentId },
                        principalTable: "Appointments",
                        principalColumns: new[] { "ClinicId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PerformedProcedures_Services_ClinicId_ServiceId",
                        columns: x => new { x.ClinicId, x.ServiceId },
                        principalTable: "Services",
                        principalColumns: new[] { "ClinicId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PerformedProcedures_Users_ClinicId_DoctorUserId",
                        columns: x => new { x.ClinicId, x.DoctorUserId },
                        principalTable: "Users",
                        principalColumns: new[] { "ClinicId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PerformedProcedures_ClinicId_DoctorUserId",
                table: "PerformedProcedures",
                columns: new[] { "ClinicId", "DoctorUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_PerformedProcedures_ClinicId_ServiceId",
                table: "PerformedProcedures",
                columns: new[] { "ClinicId", "ServiceId" });

            migrationBuilder.Sql("""
                CREATE TRIGGER "TR_PerformedProcedures_CheckAssignment"
                BEFORE INSERT ON "PerformedProcedures"
                FOR EACH ROW WHEN NOT EXISTS (
                        SELECT 1 FROM "Appointments" a
                        JOIN "DoctorProfiles" d ON d."ClinicId" = a."ClinicId" AND d."Id" = a."DoctorProfileId"
                        JOIN "Users" u ON u."ClinicId" = d."ClinicId" AND u."Id" = d."UserId"
                        JOIN "Patients" p ON p."ClinicId" = a."ClinicId" AND p."Id" = a."PatientId"
                        JOIN "DoctorServices" ds ON ds."ClinicId" = a."ClinicId" AND ds."DoctorProfileId" = a."DoctorProfileId" AND ds."ServiceId" = a."ServiceId"
                        JOIN "Services" sv ON sv."ClinicId" = a."ClinicId" AND sv."Id" = a."ServiceId"
                        WHERE a."ClinicId" = NEW."ClinicId" AND a."Id" = NEW."AppointmentId"
                          AND a."ServiceId" = NEW."ServiceId" AND sv."Name" = NEW."ServiceLabel"
                          AND a."Status" = 'Confirmed' AND a."StartUtc" <= NEW."SignedUtc"
                          AND p."IsActive" AND d."IsActive" AND u."IsActive" AND ds."IsActive" AND sv."IsActive"
                          AND u."Role" = 'Doctor' AND u."Id" = NEW."DoctorUserId"
                ) BEGIN
                    SELECT RAISE(ABORT, 'Performed procedure assignment or catalog snapshot is invalid');
                END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER "TR_PerformedProcedures_NoUpdate"
                BEFORE UPDATE ON "PerformedProcedures" FOR EACH ROW BEGIN
                    SELECT RAISE(ABORT, 'Performed procedures are append-only');
                END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER "TR_PerformedProcedures_NoDelete"
                BEFORE DELETE ON "PerformedProcedures" FOR EACH ROW BEGIN
                    SELECT RAISE(ABORT, 'Performed procedures are append-only');
                END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER "TR_PerformedProcedures_NoReplace"
                BEFORE INSERT ON "PerformedProcedures"
                FOR EACH ROW WHEN EXISTS (
                    SELECT 1 FROM "PerformedProcedures"
                    WHERE "ClinicId" = NEW."ClinicId" AND "AppointmentId" = NEW."AppointmentId" AND "RequestId" = NEW."RequestId"
                ) BEGIN
                    SELECT RAISE(ABORT, 'Performed procedures are append-only');
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PerformedProcedures");
        }
    }
}
