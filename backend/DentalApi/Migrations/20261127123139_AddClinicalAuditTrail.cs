using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DentalApi.Migrations
{
    /// <inheritdoc />
    public partial class AddClinicalAuditTrail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClinicalAuditEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ClinicId = table.Column<int>(type: "INTEGER", nullable: false),
                    AppointmentId = table.Column<int>(type: "INTEGER", nullable: false),
                    ActorUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    OccurredUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicalAuditEvents", x => x.Id);
                    table.CheckConstraint("CK_ClinicalAuditEvents_Action", "\"Action\" IN ('HistoryRead','TreatmentRecorded','TreatmentAmended')");
                    table.CheckConstraint("CK_ClinicalAuditEvents_CorrelationId", "\"CorrelationId\" <> '00000000-0000-0000-0000-000000000000' AND typeof(\"CorrelationId\") = 'text' AND length(CAST(\"CorrelationId\" AS BLOB)) = 36\n AND substr(\"CorrelationId\", 9, 1) = '-' AND substr(\"CorrelationId\", 14, 1) = '-'\n AND substr(\"CorrelationId\", 19, 1) = '-' AND substr(\"CorrelationId\", 24, 1) = '-'\n AND length(replace(\"CorrelationId\", '-', '')) = 32\n AND replace(\"CorrelationId\", '-', '') NOT GLOB '*[^0-9A-Fa-f]*'");
                    table.CheckConstraint("CK_ClinicalAuditEvents_OccurredUtc", "typeof(\"OccurredUtc\") = 'integer' AND \"OccurredUtc\" BETWEEN 1 AND 3155378975999999998");
                    table.ForeignKey(
                        name: "FK_ClinicalAuditEvents_Appointments_ClinicId_AppointmentId",
                        columns: x => new { x.ClinicId, x.AppointmentId },
                        principalTable: "Appointments",
                        principalColumns: new[] { "ClinicId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClinicalAuditEvents_Users_ClinicId_ActorUserId",
                        columns: x => new { x.ClinicId, x.ActorUserId },
                        principalTable: "Users",
                        principalColumns: new[] { "ClinicId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalAuditEvents_ClinicId_ActorUserId",
                table: "ClinicalAuditEvents",
                columns: new[] { "ClinicId", "ActorUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalAuditEvents_ClinicId_AppointmentId_OccurredUtc",
                table: "ClinicalAuditEvents",
                columns: new[] { "ClinicId", "AppointmentId", "OccurredUtc" });

            migrationBuilder.Sql("""
                CREATE TRIGGER "TR_ClinicalAuditEvents_NoUpdate"
                BEFORE UPDATE ON "ClinicalAuditEvents"
                FOR EACH ROW BEGIN
                    SELECT RAISE(ABORT, 'Clinical audit events are append-only');
                END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER "TR_ClinicalAuditEvents_NoDelete"
                BEFORE DELETE ON "ClinicalAuditEvents"
                FOR EACH ROW BEGIN
                    SELECT RAISE(ABORT, 'Clinical audit events are append-only');
                END;
                """);
            // OR REPLACE can otherwise remove the row with recursive_triggers off.
            // Id is the table's only unique key.
            migrationBuilder.Sql("""
                CREATE TRIGGER "TR_ClinicalAuditEvents_NoReplace"
                BEFORE INSERT ON "ClinicalAuditEvents"
                FOR EACH ROW WHEN EXISTS (SELECT 1 FROM "ClinicalAuditEvents" WHERE "Id" = NEW."Id")
                BEGIN
                    SELECT RAISE(ABORT, 'Clinical audit events are append-only');
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClinicalAuditEvents");
        }
    }
}
