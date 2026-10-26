using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DentalApi.Migrations
{
    /// <inheritdoc />
    public partial class AddClinicLocationVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AddressLine",
                table: "Clinics",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CountryCode",
                table: "Clinics",
                type: "TEXT",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProvinceOrCapitalCode",
                table: "Clinics",
                type: "TEXT",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SoumOrDistrictCode",
                table: "Clinics",
                type: "TEXT",
                maxLength: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TimeZoneStatus",
                table: "Clinics",
                type: "TEXT",
                nullable: false,
                defaultValue: "PendingVerification");

            migrationBuilder.AddColumn<DateTime>(
                name: "TimeZoneVerifiedAtUtc",
                table: "Clinics",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TimeZoneVerifiedByUserId",
                table: "Clinics",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TimeZoneVerifiedClinicId",
                table: "Clinics",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "Version",
                table: "Clinics",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_Clinics_TimeZoneVerifiedClinicId_TimeZoneVerifiedByUserId",
                table: "Clinics",
                columns: new[] { "TimeZoneVerifiedClinicId", "TimeZoneVerifiedByUserId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Clinics_TimeZoneVerification",
                table: "Clinics",
                sql: "(\"TimeZoneStatus\" = 'PendingVerification' AND \"TimeZoneVerifiedClinicId\" IS NULL AND \"TimeZoneVerifiedByUserId\" IS NULL AND \"TimeZoneVerifiedAtUtc\" IS NULL) OR (\"TimeZoneStatus\" = 'Verified' AND \"TimeZoneId\" IN ('Asia/Ulaanbaatar', 'Asia/Hovd') AND \"CountryCode\" IS NOT NULL AND \"CountryCode\" = 'MN' AND \"ProvinceOrCapitalCode\" IS NOT NULL AND \"SoumOrDistrictCode\" IS NOT NULL AND \"AddressLine\" IS NOT NULL AND length(trim(\"AddressLine\")) > 0 AND \"TimeZoneVerifiedClinicId\" IS NOT NULL AND \"TimeZoneVerifiedClinicId\" = \"Id\" AND \"TimeZoneVerifiedByUserId\" IS NOT NULL AND \"TimeZoneVerifiedAtUtc\" IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_Clinics_Users_TimeZoneVerifiedClinicId_TimeZoneVerifiedByUserId",
                table: "Clinics",
                columns: new[] { "TimeZoneVerifiedClinicId", "TimeZoneVerifiedByUserId" },
                principalTable: "Users",
                principalColumns: new[] { "ClinicId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Clinics_Users_TimeZoneVerifiedClinicId_TimeZoneVerifiedByUserId",
                table: "Clinics");

            migrationBuilder.DropIndex(
                name: "IX_Clinics_TimeZoneVerifiedClinicId_TimeZoneVerifiedByUserId",
                table: "Clinics");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Clinics_TimeZoneVerification",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "AddressLine",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "CountryCode",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "ProvinceOrCapitalCode",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "SoumOrDistrictCode",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "TimeZoneStatus",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "TimeZoneVerifiedAtUtc",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "TimeZoneVerifiedByUserId",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "TimeZoneVerifiedClinicId",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Clinics");
        }
    }
}
