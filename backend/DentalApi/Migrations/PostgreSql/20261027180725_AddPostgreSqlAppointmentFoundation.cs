using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DentalApi.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class AddPostgreSqlAppointmentFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Appointments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ClinicId = table.Column<int>(type: "integer", nullable: false),
                    PatientId = table.Column<int>(type: "integer", nullable: false),
                    DoctorProfileId = table.Column<int>(type: "integer", nullable: false),
                    ServiceId = table.Column<int>(type: "integer", nullable: false),
                    StartUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    OccupiedStartUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    OccupiedEndUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DurationMinutesSnapshot = table.Column<int>(type: "integer", nullable: false),
                    BufferBeforeMinutesSnapshot = table.Column<int>(type: "integer", nullable: false),
                    BufferAfterMinutesSnapshot = table.Column<int>(type: "integer", nullable: false),
                    ClinicTimeZoneIdSnapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Confirmed"),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Appointments", x => x.Id);
                    table.CheckConstraint("CK_Appointments_Interval", "\"OccupiedStartUtc\" <= \"StartUtc\" AND \"StartUtc\" < \"EndUtc\" AND \"EndUtc\" <= \"OccupiedEndUtc\"");
                    table.CheckConstraint("CK_Appointments_RequestId", "\"RequestId\" <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_Appointments_SnapshotBounds", "\"DurationMinutesSnapshot\" BETWEEN 1 AND 1440 AND \"BufferBeforeMinutesSnapshot\" BETWEEN 0 AND 1439 AND \"BufferAfterMinutesSnapshot\" BETWEEN 0 AND 1439 AND CAST(\"DurationMinutesSnapshot\" AS bigint) + \"BufferBeforeMinutesSnapshot\" + \"BufferAfterMinutesSnapshot\" <= 1440");
                    table.CheckConstraint("CK_Appointments_SnapshotIntervals", "\"EndUtc\" = \"StartUtc\" + \"DurationMinutesSnapshot\" * INTERVAL '1 minute' AND \"OccupiedStartUtc\" = \"StartUtc\" - \"BufferBeforeMinutesSnapshot\" * INTERVAL '1 minute' AND \"OccupiedEndUtc\" = \"EndUtc\" + \"BufferAfterMinutesSnapshot\" * INTERVAL '1 minute'");
                    table.CheckConstraint("CK_Appointments_Status", "\"Status\" IN ('Confirmed', 'Cancelled')");
                    table.CheckConstraint("CK_Appointments_TimeZoneSnapshot", "length(trim(\"ClinicTimeZoneIdSnapshot\")) BETWEEN 1 AND 100");
                    table.CheckConstraint("CK_Appointments_UtcRange", "isfinite(\"OccupiedStartUtc\") AND isfinite(\"StartUtc\") AND isfinite(\"EndUtc\") AND isfinite(\"OccupiedEndUtc\") AND isfinite(\"CreatedAtUtc\")");
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
                name: "IX_Appointments_ClinicId_DoctorProfileId_Status_OccupiedStartU~",
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
