using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DentalApi.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class AddPostgreSqlClinicTenantRootForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddForeignKey(
                name: "FK_Patients_Clinics_ClinicId",
                table: "Patients",
                column: "ClinicId",
                principalTable: "Clinics",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Services_Clinics_ClinicId",
                table: "Services",
                column: "ClinicId",
                principalTable: "Clinics",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Clinics_ClinicId",
                table: "Users",
                column: "ClinicId",
                principalTable: "Clinics",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Patients_Clinics_ClinicId",
                table: "Patients");

            migrationBuilder.DropForeignKey(
                name: "FK_Services_Clinics_ClinicId",
                table: "Services");

            migrationBuilder.DropForeignKey(
                name: "FK_Users_Clinics_ClinicId",
                table: "Users");
        }
    }
}
