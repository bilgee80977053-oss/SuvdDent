BEGIN TRANSACTION;
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

CREATE TABLE "ef_temp_Patients" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Patients" PRIMARY KEY AUTOINCREMENT,
    "Address" TEXT NULL,
    "Allergies" TEXT NULL,
    "ClinicId" INTEGER NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "DateOfBirth" TEXT NULL,
    "FullName" TEXT NOT NULL,
    "IsActive" INTEGER NOT NULL,
    "MedicalNotes" TEXT NULL,
    "Phone" TEXT NOT NULL,
    "UserId" INTEGER NULL,
    CONSTRAINT "FK_Patients_Users_ClinicId_UserId" FOREIGN KEY ("ClinicId", "UserId") REFERENCES "Users" ("ClinicId", "Id") ON DELETE RESTRICT
);

INSERT INTO "ef_temp_Patients" ("Id", "Address", "Allergies", "ClinicId", "CreatedAt", "DateOfBirth", "FullName", "IsActive", "MedicalNotes", "Phone", "UserId")
SELECT "Id", "Address", "Allergies", "ClinicId", "CreatedAt", "DateOfBirth", "FullName", "IsActive", "MedicalNotes", "Phone", "UserId"
FROM "Patients";

CREATE TABLE "ef_temp_Users" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Users" PRIMARY KEY AUTOINCREMENT,
    "ClinicId" INTEGER NOT NULL,
    "CreatedAt" TEXT NULL,
    "Email" TEXT NULL,
    "FullName" TEXT NOT NULL,
    "IsActive" INTEGER NOT NULL,
    "IsEmailVerified" INTEGER NOT NULL,
    "IsPhoneVerified" INTEGER NOT NULL,
    "PasswordHash" TEXT NOT NULL,
    "Phone" TEXT NULL,
    "Role" TEXT NOT NULL,
    "TokenVersion" INTEGER NOT NULL DEFAULT 0,
    CONSTRAINT "AK_Users_ClinicId_Id" UNIQUE ("ClinicId", "Id")
);

INSERT INTO "ef_temp_Users" ("Id", "ClinicId", "CreatedAt", "Email", "FullName", "IsActive", "IsEmailVerified", "IsPhoneVerified", "PasswordHash", "Phone", "Role", "TokenVersion")
SELECT "Id", "ClinicId", "CreatedAt", "Email", "FullName", "IsActive", "IsEmailVerified", "IsPhoneVerified", "PasswordHash", "Phone", "Role", "TokenVersion"
FROM "Users";

COMMIT;

PRAGMA foreign_keys = 0;

BEGIN TRANSACTION;
DROP TABLE "Patients";

ALTER TABLE "ef_temp_Patients" RENAME TO "Patients";

DROP TABLE "Users";

ALTER TABLE "ef_temp_Users" RENAME TO "Users";

COMMIT;

PRAGMA foreign_keys = 1;

BEGIN TRANSACTION;
CREATE UNIQUE INDEX "IX_Patients_ClinicId_UserId" ON "Patients" ("ClinicId", "UserId") WHERE "UserId" IS NOT NULL;

CREATE INDEX "IX_Patients_UserId" ON "Patients" ("UserId");

CREATE UNIQUE INDEX "IX_Users_ClinicId_Email" ON "Users" ("ClinicId", "Email") WHERE "Email" IS NOT NULL;

CREATE UNIQUE INDEX "IX_Users_ClinicId_Phone" ON "Users" ("ClinicId", "Phone") WHERE "Phone" IS NOT NULL;

COMMIT;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261026100808_AddPatientUserTenantIntegrity', '10.0.13');
