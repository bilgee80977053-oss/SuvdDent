using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DentalApi.Migrations
{
    /// <inheritdoc />
    public partial class AddDoctorProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DoctorProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ClinicId = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Specialty = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    PracticeStartYear = table.Column<int>(type: "INTEGER", nullable: false),
                    Biography = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    PhotoPath = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    IsPublished = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DoctorProfiles", x => x.Id);
                    table.UniqueConstraint("AK_DoctorProfiles_ClinicId_Id", x => new { x.ClinicId, x.Id });
                    table.ForeignKey(
                        name: "FK_DoctorProfiles_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DoctorProfiles_Users_ClinicId_UserId",
                        columns: x => new { x.ClinicId, x.UserId },
                        principalTable: "Users",
                        principalColumns: new[] { "ClinicId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DoctorWorkingHours",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ClinicId = table.Column<int>(type: "INTEGER", nullable: false),
                    DoctorProfileId = table.Column<int>(type: "INTEGER", nullable: false),
                    DayOfWeek = table.Column<int>(type: "INTEGER", nullable: false),
                    StartMinute = table.Column<int>(type: "INTEGER", nullable: false),
                    EndMinute = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DoctorWorkingHours", x => x.Id);
                    table.CheckConstraint("CK_DoctorWorkingHours_Interval", "\"DayOfWeek\" BETWEEN 0 AND 6 AND \"StartMinute\" >= 0 AND \"StartMinute\" < \"EndMinute\" AND \"EndMinute\" <= 1440");
                    table.ForeignKey(
                        name: "FK_DoctorWorkingHours_DoctorProfiles_ClinicId_DoctorProfileId",
                        columns: x => new { x.ClinicId, x.DoctorProfileId },
                        principalTable: "DoctorProfiles",
                        principalColumns: new[] { "ClinicId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DoctorProfiles_ClinicId_UserId",
                table: "DoctorProfiles",
                columns: new[] { "ClinicId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DoctorWorkingHours_ClinicId_DoctorProfileId_DayOfWeek_StartMinute",
                table: "DoctorWorkingHours",
                columns: new[] { "ClinicId", "DoctorProfileId", "DayOfWeek", "StartMinute" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DoctorWorkingHours");

            migrationBuilder.DropTable(
                name: "DoctorProfiles");
        }
    }
}
