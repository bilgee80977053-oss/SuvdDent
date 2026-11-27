using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DentalApi.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class AddPostgreSqlAppointmentReschedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppointmentReschedules",
                columns: table => new
                {
                    ClinicId = table.Column<int>(type: "integer", nullable: false),
                    SourceId = table.Column<int>(type: "integer", nullable: false),
                    ReplacementId = table.Column<int>(type: "integer", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppointmentReschedules", x => new { x.ClinicId, x.SourceId });
                    table.CheckConstraint("CK_AppointmentReschedules_Distinct", "\"SourceId\" <> \"ReplacementId\"");
                    table.CheckConstraint("CK_AppointmentReschedules_RequestId", "\"RequestId\" <> '00000000-0000-0000-0000-000000000000'");
                    table.ForeignKey(
                        name: "FK_AppointmentReschedules_Appointments_ClinicId_ReplacementId",
                        columns: x => new { x.ClinicId, x.ReplacementId },
                        principalTable: "Appointments",
                        principalColumns: new[] { "ClinicId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppointmentReschedules_Appointments_ClinicId_SourceId",
                        columns: x => new { x.ClinicId, x.SourceId },
                        principalTable: "Appointments",
                        principalColumns: new[] { "ClinicId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentReschedules_ClinicId_ReplacementId",
                table: "AppointmentReschedules",
                columns: new[] { "ClinicId", "ReplacementId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppointmentReschedules");
        }
    }
}
