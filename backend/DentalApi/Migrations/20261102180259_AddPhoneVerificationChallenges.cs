using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DentalApi.Migrations
{
    /// <inheritdoc />
    public partial class AddPhoneVerificationChallenges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PhoneVerificationChallenge",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClinicId = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    PhoneSnapshot = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Destination = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                    CodeHash = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CreatedUtcTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpiresUtcTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    FailedAttempts = table.Column<int>(type: "INTEGER", nullable: false),
                    LastFailedUtcTicks = table.Column<long>(type: "INTEGER", nullable: true),
                    DeliveryState = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    IsConsumed = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsInvalidated = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhoneVerificationChallenge", x => x.Id);
                    table.CheckConstraint("CK_PhoneVerificationChallenge_Attempts", "\"FailedAttempts\" BETWEEN 0 AND 5");
                    table.CheckConstraint("CK_PhoneVerificationChallenge_Consumed", "NOT \"IsConsumed\" OR \"DeliveryState\" = 'Accepted'");
                    table.CheckConstraint("CK_PhoneVerificationChallenge_Delivery", "\"DeliveryState\" IN ('Pending','Accepted','Failed')");
                    table.CheckConstraint("CK_PhoneVerificationChallenge_Expiry", "\"CreatedUtcTicks\" >= 0 AND \"ExpiresUtcTicks\" - \"CreatedUtcTicks\" = 1200000000");
                    table.ForeignKey(
                        name: "FK_PhoneVerificationChallenge_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PhoneVerificationChallenge_Users_ClinicId_UserId",
                        columns: x => new { x.ClinicId, x.UserId },
                        principalTable: "Users",
                        principalColumns: new[] { "ClinicId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PhoneVerificationChallenge_ClinicId_UserId",
                table: "PhoneVerificationChallenge",
                columns: new[] { "ClinicId", "UserId" },
                unique: true,
                filter: "NOT \"IsConsumed\" AND NOT \"IsInvalidated\"");

            migrationBuilder.CreateIndex(
                name: "IX_PhoneVerificationChallenge_ClinicId_UserId_CreatedUtcTicks",
                table: "PhoneVerificationChallenge",
                columns: new[] { "ClinicId", "UserId", "CreatedUtcTicks" });

            migrationBuilder.CreateIndex(
                name: "IX_PhoneVerificationChallenge_ClinicId_UserId_LastFailedUtcTicks",
                table: "PhoneVerificationChallenge",
                columns: new[] { "ClinicId", "UserId", "LastFailedUtcTicks" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PhoneVerificationChallenge");
        }
    }
}
