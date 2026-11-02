using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DentalApi.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_Appointments_ClinicId_Id",
                table: "Appointments",
                columns: new[] { "ClinicId", "Id" });

            migrationBuilder.CreateTable(
                name: "NotificationOutbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClinicId = table.Column<int>(type: "INTEGER", nullable: false),
                    AppointmentId = table.Column<int>(type: "INTEGER", nullable: false),
                    Event = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Channel = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Recipient = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    Body = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    DueUtcTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    NextAttemptUtcTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    SentUtcTicks = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationOutbox", x => x.Id);
                    table.CheckConstraint("CK_NotificationOutbox_Attempts", "\"Attempts\" BETWEEN 0 AND 8");
                    table.CheckConstraint("CK_NotificationOutbox_Channel", "\"Channel\" IN ('Email','Sms')");
                    table.CheckConstraint("CK_NotificationOutbox_Event", "\"Event\" IN ('Confirmed','Cancelled','Reminder')");
                    table.CheckConstraint("CK_NotificationOutbox_Reminder", "\"Event\" <> 'Reminder' OR \"Channel\" = 'Sms'");
                    table.CheckConstraint("CK_NotificationOutbox_State", "\"State\" IN ('Pending','Sent','Suppressed','Failed')");
                    table.ForeignKey(
                        name: "FK_NotificationOutbox_Appointments_ClinicId_AppointmentId",
                        columns: x => new { x.ClinicId, x.AppointmentId },
                        principalTable: "Appointments",
                        principalColumns: new[] { "ClinicId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NotificationPreference",
                columns: table => new
                {
                    ClinicId = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    EmailEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    SmsEnabled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationPreference", x => new { x.ClinicId, x.UserId });
                    table.ForeignKey(
                        name: "FK_NotificationPreference_Users_ClinicId_UserId",
                        columns: x => new { x.ClinicId, x.UserId },
                        principalTable: "Users",
                        principalColumns: new[] { "ClinicId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationOutbox_ClinicId_AppointmentId_Event_Channel",
                table: "NotificationOutbox",
                columns: new[] { "ClinicId", "AppointmentId", "Event", "Channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationOutbox_State_NextAttemptUtcTicks",
                table: "NotificationOutbox",
                columns: new[] { "State", "NextAttemptUtcTicks" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotificationOutbox");

            migrationBuilder.DropTable(
                name: "NotificationPreference");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Appointments_ClinicId_Id",
                table: "Appointments");
        }
    }
}
