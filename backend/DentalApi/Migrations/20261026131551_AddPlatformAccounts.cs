using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DentalApi.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlatformAccounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FullName = table.Column<string>(type: "TEXT", nullable: false),
                    Email = table.Column<string>(type: "TEXT", nullable: true, collation: "NOCASE"),
                    Phone = table.Column<string>(type: "TEXT", nullable: true),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsEmailVerified = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsPhoneVerified = table.Column<bool>(type: "INTEGER", nullable: false),
                    TokenVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformAccounts", x => x.Id);
                    table.CheckConstraint("CK_PlatformAccounts_Contacts", "(\"Email\" IS NOT NULL OR \"Phone\" IS NOT NULL) AND \"TokenVersion\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "ClinicPatientLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlatformAccountId = table.Column<int>(type: "INTEGER", nullable: false),
                    ClinicId = table.Column<int>(type: "INTEGER", nullable: false),
                    PatientId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicPatientLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClinicPatientLinks_Patients_ClinicId_PatientId",
                        columns: x => new { x.ClinicId, x.PatientId },
                        principalTable: "Patients",
                        principalColumns: new[] { "ClinicId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClinicPatientLinks_PlatformAccounts_PlatformAccountId",
                        column: x => x.PlatformAccountId,
                        principalTable: "PlatformAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlatformVerificationCodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlatformAccountId = table.Column<int>(type: "INTEGER", nullable: false),
                    Channel = table.Column<string>(type: "TEXT", nullable: false),
                    Purpose = table.Column<string>(type: "TEXT", nullable: false),
                    CodeHash = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FailedAttempts = table.Column<int>(type: "INTEGER", nullable: false),
                    IsUsed = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformVerificationCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlatformVerificationCodes_PlatformAccounts_PlatformAccountId",
                        column: x => x.PlatformAccountId,
                        principalTable: "PlatformAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicPatientLinks_ClinicId_PatientId",
                table: "ClinicPatientLinks",
                columns: new[] { "ClinicId", "PatientId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClinicPatientLinks_PlatformAccountId_ClinicId",
                table: "ClinicPatientLinks",
                columns: new[] { "PlatformAccountId", "ClinicId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlatformAccounts_Email",
                table: "PlatformAccounts",
                column: "Email",
                unique: true,
                filter: "\"Email\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformAccounts_Phone",
                table: "PlatformAccounts",
                column: "Phone",
                unique: true,
                filter: "\"Phone\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformVerificationCodes_PlatformAccountId_Channel_Purpose_CreatedAt",
                table: "PlatformVerificationCodes",
                columns: new[] { "PlatformAccountId", "Channel", "Purpose", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClinicPatientLinks");

            migrationBuilder.DropTable(
                name: "PlatformVerificationCodes");

            migrationBuilder.DropTable(
                name: "PlatformAccounts");
        }
    }
}
