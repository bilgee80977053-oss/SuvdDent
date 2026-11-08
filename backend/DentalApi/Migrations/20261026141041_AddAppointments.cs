using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DentalApi.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_ClinicPatientLinks_ClinicId_PatientId_PlatformAccountId",
                table: "ClinicPatientLinks",
                columns: new[] { "ClinicId", "PatientId", "PlatformAccountId" });

            migrationBuilder.CreateTable(
                name: "Appointments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ClinicId = table.Column<int>(type: "INTEGER", nullable: false),
                    DoctorProfileId = table.Column<int>(type: "INTEGER", nullable: false),
                    ServiceId = table.Column<int>(type: "INTEGER", nullable: false),
                    PatientId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlatformAccountId = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    StartUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    EndUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    OccupiedStartUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    OccupiedEndUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    DurationMinutesSnapshot = table.Column<int>(type: "INTEGER", nullable: false),
                    BufferBeforeMinutesSnapshot = table.Column<int>(type: "INTEGER", nullable: false),
                    BufferAfterMinutesSnapshot = table.Column<int>(type: "INTEGER", nullable: false),
                    TimeZoneIdSnapshot = table.Column<string>(type: "TEXT", nullable: false),
                    DoctorNameSnapshot = table.Column<string>(type: "TEXT", nullable: false),
                    ServiceNameSnapshot = table.Column<string>(type: "TEXT", nullable: false),
                    MinPriceSnapshot = table.Column<long>(type: "INTEGER", nullable: false),
                    MaxPriceSnapshot = table.Column<long>(type: "INTEGER", nullable: true),
                    ClinicVersionSnapshot = table.Column<Guid>(type: "TEXT", nullable: false),
                    DoctorVersionSnapshot = table.Column<Guid>(type: "TEXT", nullable: false),
                    IdempotencyKey = table.Column<Guid>(type: "TEXT", nullable: false),
                    RequestHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PatientCreationConsentAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PatientCreationConsentVersion = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Appointments", x => x.Id);
                    table.CheckConstraint("CK_Appointments_Snapshot", "\"Status\" IN ('Confirmed','Cancelled') AND \"StartUtc\" >= 0 AND \"StartUtc\" < \"EndUtc\" AND \"OccupiedStartUtc\" >= 0 AND \"OccupiedStartUtc\" <= \"StartUtc\" AND \"OccupiedEndUtc\" >= \"EndUtc\" AND \"OccupiedEndUtc\" <= 3155378975999999999 AND \"DurationMinutesSnapshot\" > 0 AND \"BufferBeforeMinutesSnapshot\" >= 0 AND \"BufferAfterMinutesSnapshot\" >= 0 AND \"DurationMinutesSnapshot\" + \"BufferBeforeMinutesSnapshot\" + \"BufferAfterMinutesSnapshot\" <= 1440 AND \"EndUtc\" - \"StartUtc\" = \"DurationMinutesSnapshot\" * 600000000 AND \"StartUtc\" - \"OccupiedStartUtc\" = \"BufferBeforeMinutesSnapshot\" * 600000000 AND \"OccupiedEndUtc\" - \"EndUtc\" = \"BufferAfterMinutesSnapshot\" * 600000000 AND \"TimeZoneIdSnapshot\" IN ('Asia/Ulaanbaatar','Asia/Hovd')");
                    table.ForeignKey(
                        name: "FK_Appointments_ClinicPatientLinks_ClinicId_PatientId_PlatformAccountId",
                        columns: x => new { x.ClinicId, x.PatientId, x.PlatformAccountId },
                        principalTable: "ClinicPatientLinks",
                        principalColumns: new[] { "ClinicId", "PatientId", "PlatformAccountId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Appointments_DoctorProfiles_ClinicId_DoctorProfileId",
                        columns: x => new { x.ClinicId, x.DoctorProfileId },
                        principalTable: "DoctorProfiles",
                        principalColumns: new[] { "ClinicId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Appointments_Services_ClinicId_ServiceId",
                        columns: x => new { x.ClinicId, x.ServiceId },
                        principalTable: "Services",
                        principalColumns: new[] { "ClinicId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_ClinicId_DoctorProfileId_Status_OccupiedStartUtc",
                table: "Appointments",
                columns: new[] { "ClinicId", "DoctorProfileId", "Status", "OccupiedStartUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_ClinicId_PatientId_PlatformAccountId",
                table: "Appointments",
                columns: new[] { "ClinicId", "PatientId", "PlatformAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_ClinicId_ServiceId",
                table: "Appointments",
                columns: new[] { "ClinicId", "ServiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_PlatformAccountId_IdempotencyKey",
                table: "Appointments",
                columns: new[] { "PlatformAccountId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_PlatformAccountId_Status_StartUtc",
                table: "Appointments",
                columns: new[] { "PlatformAccountId", "Status", "StartUtc" });
            migrationBuilder.Sql("""
                CREATE TRIGGER TR_Appointments_NoOverlap_INSERT
                BEFORE INSERT ON Appointments
                WHEN NEW.Status = 'Confirmed'
                BEGIN
                  SELECT RAISE(ABORT, 'APPOINTMENT_OVERLAP') WHERE EXISTS (
                    SELECT 1 FROM Appointments a WHERE a.Id <> NEW.Id AND a.Status = 'Confirmed' AND
                    ((a.ClinicId = NEW.ClinicId AND a.DoctorProfileId = NEW.DoctorProfileId AND
                      a.OccupiedStartUtc < NEW.OccupiedEndUtc AND NEW.OccupiedStartUtc < a.OccupiedEndUtc) OR
                     (a.PlatformAccountId = NEW.PlatformAccountId AND a.StartUtc < NEW.EndUtc AND NEW.StartUtc < a.EndUtc))
                  );
                END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER TR_Appointments_NoOverlap_UPDATE
                BEFORE UPDATE ON Appointments
                WHEN NEW.Status = 'Confirmed'
                BEGIN
                  SELECT RAISE(ABORT, 'APPOINTMENT_OVERLAP') WHERE EXISTS (
                    SELECT 1 FROM Appointments a WHERE a.Id <> NEW.Id AND a.Status = 'Confirmed' AND
                    ((a.ClinicId = NEW.ClinicId AND a.DoctorProfileId = NEW.DoctorProfileId AND
                      a.OccupiedStartUtc < NEW.OccupiedEndUtc AND NEW.OccupiedStartUtc < a.OccupiedEndUtc) OR
                     (a.PlatformAccountId = NEW.PlatformAccountId AND a.StartUtc < NEW.EndUtc AND NEW.StartUtc < a.EndUtc))
                  );
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_Appointments_NoOverlap_INSERT;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_Appointments_NoOverlap_UPDATE;");
            migrationBuilder.DropTable(
                name: "Appointments");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_ClinicPatientLinks_ClinicId_PatientId_PlatformAccountId",
                table: "ClinicPatientLinks");
        }
    }
}
