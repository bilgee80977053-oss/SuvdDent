using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DentalApi.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class AddPostgreSqlClinicalAuditTrail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClinicalAuditEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ClinicId = table.Column<int>(type: "integer", nullable: false),
                    AppointmentId = table.Column<int>(type: "integer", nullable: false),
                    ActorUserId = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    OccurredUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicalAuditEvents", x => x.Id);
                    table.CheckConstraint("CK_ClinicalAuditEvents_Action", "\"Action\" IN ('HistoryRead','TreatmentRecorded','TreatmentAmended')");
                    table.CheckConstraint("CK_ClinicalAuditEvents_CorrelationId", "\"CorrelationId\" <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_ClinicalAuditEvents_OccurredUtc", "isfinite(\"OccurredUtc\")");
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
                CREATE FUNCTION "RejectClinicalAuditMutation"() RETURNS trigger
                LANGUAGE plpgsql
                AS $clinical_audit$
                BEGIN
                    RAISE EXCEPTION USING ERRCODE = '55000', MESSAGE = 'Clinical audit events are append-only';
                END;
                $clinical_audit$;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER "TR_ClinicalAuditEvents_NoMutation"
                BEFORE UPDATE OR DELETE ON "ClinicalAuditEvents"
                FOR EACH ROW EXECUTE FUNCTION "RejectClinicalAuditMutation"();
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER "TR_ClinicalAuditEvents_NoTruncate"
                BEFORE TRUNCATE ON "ClinicalAuditEvents"
                FOR EACH STATEMENT EXECUTE FUNCTION "RejectClinicalAuditMutation"();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClinicalAuditEvents");
            migrationBuilder.Sql("""DROP FUNCTION "RejectClinicalAuditMutation"();""");
        }
    }
}
