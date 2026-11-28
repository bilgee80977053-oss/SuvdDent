using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DentalApi.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class AddPostgreSqlPerformedProcedures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PerformedProcedures",
                columns: table => new
                {
                    ClinicId = table.Column<int>(type: "integer", nullable: false),
                    AppointmentId = table.Column<int>(type: "integer", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    DoctorUserId = table.Column<int>(type: "integer", nullable: false),
                    ServiceId = table.Column<int>(type: "integer", nullable: false),
                    ServiceLabel = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    TargetScope = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    ToothCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    SignedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PerformedProcedures", x => new { x.ClinicId, x.AppointmentId, x.RequestId });
                    table.CheckConstraint("CK_PerformedProcedures_Label", "length(trim(\"ServiceLabel\")) BETWEEN 2 AND 160");
                    table.CheckConstraint("CK_PerformedProcedures_RequestId", "\"RequestId\" <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_PerformedProcedures_SignedUtc", "isfinite(\"SignedUtc\")");
                    table.CheckConstraint("CK_PerformedProcedures_Target", "(\"TargetScope\" = 'Visit' AND \"ToothCode\" IS NULL) OR\n(\"TargetScope\" = 'SingleTooth' AND \"ToothCode\" IS NOT NULL AND\n    ((\"ToothCode\" COLLATE \"C\") ~ '^([1-4][1-8]|[5-8][1-5])$'))");
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
                CREATE FUNCTION "GuardPerformedProcedure"() RETURNS trigger
                LANGUAGE plpgsql
                AS $performed_guard$
                BEGIN
                    IF TG_OP = 'INSERT' THEN
                        IF NOT EXISTS (
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
                        ) THEN
                            RAISE EXCEPTION USING ERRCODE = '23514', MESSAGE = 'Performed procedure assignment or catalog snapshot is invalid';
                        END IF;
                        RETURN NEW;
                    END IF;
                    RAISE EXCEPTION USING ERRCODE = '55000', MESSAGE = 'Performed procedures are append-only';
                END;
                $performed_guard$;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER "TR_PerformedProcedures_CheckAssignment"
                BEFORE INSERT ON "PerformedProcedures" FOR EACH ROW EXECUTE FUNCTION "GuardPerformedProcedure"();
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER "TR_PerformedProcedures_NoMutation"
                BEFORE UPDATE OR DELETE ON "PerformedProcedures" FOR EACH ROW EXECUTE FUNCTION "GuardPerformedProcedure"();
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER "TR_PerformedProcedures_NoTruncate"
                BEFORE TRUNCATE ON "PerformedProcedures" FOR EACH STATEMENT EXECUTE FUNCTION "GuardPerformedProcedure"();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PerformedProcedures");
            migrationBuilder.Sql("""DROP FUNCTION "GuardPerformedProcedure"();""");
        }
    }
}
