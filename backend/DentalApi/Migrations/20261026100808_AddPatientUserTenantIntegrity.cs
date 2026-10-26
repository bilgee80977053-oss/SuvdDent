using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DentalApi.Migrations
{
    /// <inheritdoc />
    public partial class AddPatientUserTenantIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Abort on existing mismatches; never delete, reassign, or repair rows.
            migrationBuilder.Sql("""
                CREATE TEMP TABLE "__PatientUserTenantIntegrityCheck" (
                    "InvalidCount" INTEGER NOT NULL
                    CONSTRAINT "CK_PatientUserTenantIntegrity" CHECK ("InvalidCount" = 0)
                );
                INSERT INTO "__PatientUserTenantIntegrityCheck" ("InvalidCount")
                SELECT COUNT(*)
                FROM "Patients" p
                LEFT JOIN "Users" u ON u."Id" = p."UserId"
                WHERE p."UserId" IS NOT NULL
                  AND (u."Id" IS NULL OR p."ClinicId" <> u."ClinicId");
                DROP TABLE "__PatientUserTenantIntegrityCheck";

                -- Support the new Patients FK during SQLite's copy phase,
                -- before Users is rebuilt with its permanent alternate key.
                -- Dropping the old Users table also removes this helper index.
                CREATE UNIQUE INDEX "IX_Users_TenantIntegrityMigration"
                    ON "Users" ("ClinicId", "Id");
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_Patients_Users_UserId",
                table: "Patients");

            migrationBuilder.AlterColumn<int>(
                name: "TokenVersion",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Users_ClinicId_Id",
                table: "Users",
                columns: new[] { "ClinicId", "Id" });

            migrationBuilder.AddForeignKey(
                name: "FK_Patients_Users_ClinicId_UserId",
                table: "Patients",
                columns: new[] { "ClinicId", "UserId" },
                principalTable: "Users",
                principalColumns: new[] { "ClinicId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Patients_Users_ClinicId_UserId",
                table: "Patients");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Users_ClinicId_Id",
                table: "Users");

            migrationBuilder.AlterColumn<int>(
                name: "TokenVersion",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldDefaultValue: 0);

            migrationBuilder.AddForeignKey(
                name: "FK_Patients_Users_UserId",
                table: "Patients",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
