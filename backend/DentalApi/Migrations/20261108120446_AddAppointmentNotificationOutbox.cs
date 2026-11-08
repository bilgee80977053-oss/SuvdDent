using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DentalApi.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentNotificationOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SQLite accepts a unique parent index for this composite FK. Avoid
            // rebuilding Appointments, which would otherwise drop custom overlap triggers.
            migrationBuilder.CreateIndex(
                name: "AK_Appointments_ClinicId_PlatformAccountId_Id",
                table: "Appointments",
                columns: new[] { "ClinicId", "PlatformAccountId", "Id" },
                unique: true);

            migrationBuilder.CreateTable(
                name: "AppointmentNotificationOutbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AppointmentId = table.Column<int>(type: "INTEGER", nullable: false),
                    ClinicId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlatformAccountId = table.Column<int>(type: "INTEGER", nullable: false),
                    Channel = table.Column<string>(type: "TEXT", nullable: false),
                    Mode = table.Column<string>(type: "TEXT", nullable: false),
                    TemplateVersion = table.Column<string>(type: "TEXT", nullable: false),
                    Subject = table.Column<string>(type: "TEXT", nullable: true),
                    Body = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LeaseToken = table.Column<Guid>(type: "TEXT", nullable: true),
                    LeaseExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastErrorCode = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppointmentNotificationOutbox", x => x.Id);
                    table.CheckConstraint("CK_AppointmentNotificationOutbox_State", "\"Mode\" = 'Demo' AND \"Channel\" IN ('Email','Sms') AND \"Status\" IN ('Pending','Processing','DemoGenerated','Failed') AND \"AttemptCount\" BETWEEN 0 AND 3 AND ((\"Status\" = 'Processing' AND \"LeaseToken\" IS NOT NULL AND \"LeaseExpiresAtUtc\" IS NOT NULL) OR (\"Status\" <> 'Processing' AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL))");
                    table.ForeignKey(
                        name: "FK_AppointmentNotificationOutbox_Appointments_ClinicId_PlatformAccountId_AppointmentId",
                        columns: x => new { x.ClinicId, x.PlatformAccountId, x.AppointmentId },
                        principalTable: "Appointments",
                        principalColumns: new[] { "ClinicId", "PlatformAccountId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentNotificationOutbox_AppointmentId_Channel",
                table: "AppointmentNotificationOutbox",
                columns: new[] { "AppointmentId", "Channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentNotificationOutbox_ClinicId_PlatformAccountId_AppointmentId",
                table: "AppointmentNotificationOutbox",
                columns: new[] { "ClinicId", "PlatformAccountId", "AppointmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentNotificationOutbox_CreatedAtUtc",
                table: "AppointmentNotificationOutbox",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentNotificationOutbox_Status_NextAttemptAtUtc",
                table: "AppointmentNotificationOutbox",
                columns: new[] { "Status", "NextAttemptAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppointmentNotificationOutbox");

            migrationBuilder.DropIndex(
                name: "AK_Appointments_ClinicId_PlatformAccountId_Id",
                table: "Appointments");
        }
    }
}
