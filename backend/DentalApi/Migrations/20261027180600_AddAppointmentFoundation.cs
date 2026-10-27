using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DentalApi.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Appointments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ClinicId = table.Column<int>(type: "INTEGER", nullable: false),
                    PatientId = table.Column<int>(type: "INTEGER", nullable: false),
                    DoctorProfileId = table.Column<int>(type: "INTEGER", nullable: false),
                    ServiceId = table.Column<int>(type: "INTEGER", nullable: false),
                    StartUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    EndUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    OccupiedStartUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    OccupiedEndUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    DurationMinutesSnapshot = table.Column<int>(type: "INTEGER", nullable: false),
                    BufferBeforeMinutesSnapshot = table.Column<int>(type: "INTEGER", nullable: false),
                    BufferAfterMinutesSnapshot = table.Column<int>(type: "INTEGER", nullable: false),
                    ClinicTimeZoneIdSnapshot = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false, defaultValue: "Confirmed"),
                    RequestId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Appointments", x => x.Id);
                    table.CheckConstraint("CK_Appointments_Interval", "\"OccupiedStartUtc\" <= \"StartUtc\" AND \"StartUtc\" < \"EndUtc\" AND \"EndUtc\" <= \"OccupiedEndUtc\"");
                    table.CheckConstraint("CK_Appointments_RequestId", "\"RequestId\" <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_Appointments_SnapshotBounds", "\"DurationMinutesSnapshot\" BETWEEN 1 AND 1440 AND \"BufferBeforeMinutesSnapshot\" BETWEEN 0 AND 1439 AND \"BufferAfterMinutesSnapshot\" BETWEEN 0 AND 1439 AND CAST(\"DurationMinutesSnapshot\" AS bigint) + \"BufferBeforeMinutesSnapshot\" + \"BufferAfterMinutesSnapshot\" <= 1440");
                    table.CheckConstraint("CK_Appointments_SnapshotIntervals", "\"EndUtc\" - \"StartUtc\" = \"DurationMinutesSnapshot\" * 600000000 AND \"StartUtc\" - \"OccupiedStartUtc\" = \"BufferBeforeMinutesSnapshot\" * 600000000 AND \"OccupiedEndUtc\" - \"EndUtc\" = \"BufferAfterMinutesSnapshot\" * 600000000");
                    table.CheckConstraint("CK_Appointments_Status", "\"Status\" IN ('Confirmed', 'Cancelled')");
                    table.CheckConstraint("CK_Appointments_TimeZoneSnapshot", "length(trim(\"ClinicTimeZoneIdSnapshot\")) BETWEEN 1 AND 100");
                    table.CheckConstraint("CK_Appointments_UtcRange", "\"OccupiedStartUtc\" >= 0 AND \"OccupiedEndUtc\" <= 3155378975999999999 AND \"CreatedAtUtc\" BETWEEN 0 AND 3155378975999999999");
                    table.ForeignKey(
                        name: "FK_Appointments_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Appointments_DoctorProfiles_ClinicId_DoctorProfileId",
                        columns: x => new { x.ClinicId, x.DoctorProfileId },
                        principalTable: "DoctorProfiles",
                        principalColumns: new[] { "ClinicId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Appointments_Patients_ClinicId_PatientId",
                        columns: x => new { x.ClinicId, x.PatientId },
                        principalTable: "Patients",
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
                name: "IX_Appointments_ClinicId_DoctorProfileId_Status_OccupiedStartUtc_OccupiedEndUtc",
                table: "Appointments",
                columns: new[] { "ClinicId", "DoctorProfileId", "Status", "OccupiedStartUtc", "OccupiedEndUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_ClinicId_PatientId_RequestId",
                table: "Appointments",
                columns: new[] { "ClinicId", "PatientId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_ClinicId_PatientId_StartUtc",
                table: "Appointments",
                columns: new[] { "ClinicId", "PatientId", "StartUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_ClinicId_ServiceId",
                table: "Appointments",
                columns: new[] { "ClinicId", "ServiceId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Appointments");
        }
    }
}
