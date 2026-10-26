using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DentalApi.Migrations
{
    /// <inheritdoc />
    public partial class AddSchedulingFoundations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "Clinics",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "Asia/Ulaanbaatar");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Services_ClinicId_Id",
                table: "Services",
                columns: new[] { "ClinicId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Patients_ClinicId_Id",
                table: "Patients",
                columns: new[] { "ClinicId", "Id" });

            migrationBuilder.CreateTable(
                name: "DoctorServices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ClinicId = table.Column<int>(type: "INTEGER", nullable: false),
                    DoctorProfileId = table.Column<int>(type: "INTEGER", nullable: false),
                    ServiceId = table.Column<int>(type: "INTEGER", nullable: false),
                    DurationMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    BufferBeforeMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    BufferAfterMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DoctorServices", x => x.Id);
                    table.CheckConstraint("CK_DoctorServices_Duration", "\"DurationMinutes\" > 0 AND \"BufferBeforeMinutes\" >= 0 AND \"BufferAfterMinutes\" >= 0 AND \"DurationMinutes\" + \"BufferBeforeMinutes\" + \"BufferAfterMinutes\" <= 1440");
                    table.ForeignKey(
                        name: "FK_DoctorServices_DoctorProfiles_ClinicId_DoctorProfileId",
                        columns: x => new { x.ClinicId, x.DoctorProfileId },
                        principalTable: "DoctorProfiles",
                        principalColumns: new[] { "ClinicId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DoctorServices_Services_ClinicId_ServiceId",
                        columns: x => new { x.ClinicId, x.ServiceId },
                        principalTable: "Services",
                        principalColumns: new[] { "ClinicId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PatientAvailabilitySchedules",
                columns: table => new
                {
                    ClinicId = table.Column<int>(type: "INTEGER", nullable: false),
                    PatientId = table.Column<int>(type: "INTEGER", nullable: false),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatientAvailabilitySchedules", x => new { x.ClinicId, x.PatientId });
                    table.ForeignKey(
                        name: "FK_PatientAvailabilitySchedules_Patients_ClinicId_PatientId",
                        columns: x => new { x.ClinicId, x.PatientId },
                        principalTable: "Patients",
                        principalColumns: new[] { "ClinicId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PatientAvailabilities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ClinicId = table.Column<int>(type: "INTEGER", nullable: false),
                    PatientId = table.Column<int>(type: "INTEGER", nullable: false),
                    StartUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    EndUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatientAvailabilities", x => x.Id);
                    table.CheckConstraint("CK_PatientAvailabilities_Interval", "\"StartUtc\" >= 0 AND \"StartUtc\" < \"EndUtc\" AND \"EndUtc\" <= 3155378975999999999");
                    table.ForeignKey(
                        name: "FK_PatientAvailabilities_PatientAvailabilitySchedules_ClinicId_PatientId",
                        columns: x => new { x.ClinicId, x.PatientId },
                        principalTable: "PatientAvailabilitySchedules",
                        principalColumns: new[] { "ClinicId", "PatientId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DoctorServices_ClinicId_DoctorProfileId_ServiceId",
                table: "DoctorServices",
                columns: new[] { "ClinicId", "DoctorProfileId", "ServiceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DoctorServices_ClinicId_ServiceId",
                table: "DoctorServices",
                columns: new[] { "ClinicId", "ServiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_PatientAvailabilities_ClinicId_PatientId_StartUtc",
                table: "PatientAvailabilities",
                columns: new[] { "ClinicId", "PatientId", "StartUtc" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DoctorServices");

            migrationBuilder.DropTable(
                name: "PatientAvailabilities");

            migrationBuilder.DropTable(
                name: "PatientAvailabilitySchedules");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Services_ClinicId_Id",
                table: "Services");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Patients_ClinicId_Id",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "Clinics");
        }
    }
}
