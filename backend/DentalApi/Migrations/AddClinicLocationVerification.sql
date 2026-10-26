BEGIN TRANSACTION;
ALTER TABLE "Clinics" ADD "AddressLine" TEXT NULL;

ALTER TABLE "Clinics" ADD "CountryCode" TEXT NULL;

ALTER TABLE "Clinics" ADD "ProvinceOrCapitalCode" TEXT NULL;

ALTER TABLE "Clinics" ADD "SoumOrDistrictCode" TEXT NULL;

ALTER TABLE "Clinics" ADD "TimeZoneStatus" TEXT NOT NULL DEFAULT 'PendingVerification';

ALTER TABLE "Clinics" ADD "TimeZoneVerifiedAtUtc" TEXT NULL;

ALTER TABLE "Clinics" ADD "TimeZoneVerifiedByUserId" INTEGER NULL;

ALTER TABLE "Clinics" ADD "TimeZoneVerifiedClinicId" INTEGER NULL;

ALTER TABLE "Clinics" ADD "Version" TEXT NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';

CREATE INDEX "IX_Clinics_TimeZoneVerifiedClinicId_TimeZoneVerifiedByUserId" ON "Clinics" ("TimeZoneVerifiedClinicId", "TimeZoneVerifiedByUserId");

CREATE TABLE "ef_temp_Clinics" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Clinics" PRIMARY KEY AUTOINCREMENT,
    "AddressLine" TEXT NULL,
    "CountryCode" TEXT NULL,
    "Name" TEXT NOT NULL,
    "Phone" TEXT NULL,
    "ProvinceOrCapitalCode" TEXT NULL,
    "SoumOrDistrictCode" TEXT NULL,
    "TimeZoneId" TEXT NOT NULL DEFAULT 'Asia/Ulaanbaatar',
    "TimeZoneStatus" TEXT NOT NULL DEFAULT 'PendingVerification',
    "TimeZoneVerifiedAtUtc" TEXT NULL,
    "TimeZoneVerifiedByUserId" INTEGER NULL,
    "TimeZoneVerifiedClinicId" INTEGER NULL,
    "Version" TEXT NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',
    "WorkHours" TEXT NULL,
    CONSTRAINT "CK_Clinics_TimeZoneVerification" CHECK (("TimeZoneStatus" = 'PendingVerification' AND "TimeZoneVerifiedClinicId" IS NULL AND "TimeZoneVerifiedByUserId" IS NULL AND "TimeZoneVerifiedAtUtc" IS NULL) OR ("TimeZoneStatus" = 'Verified' AND "TimeZoneId" IN ('Asia/Ulaanbaatar', 'Asia/Hovd') AND "CountryCode" IS NOT NULL AND "CountryCode" = 'MN' AND "ProvinceOrCapitalCode" IS NOT NULL AND "SoumOrDistrictCode" IS NOT NULL AND "AddressLine" IS NOT NULL AND length(trim("AddressLine")) > 0 AND "TimeZoneVerifiedClinicId" IS NOT NULL AND "TimeZoneVerifiedClinicId" = "Id" AND "TimeZoneVerifiedByUserId" IS NOT NULL AND "TimeZoneVerifiedAtUtc" IS NOT NULL)),
    CONSTRAINT "FK_Clinics_Users_TimeZoneVerifiedClinicId_TimeZoneVerifiedByUserId" FOREIGN KEY ("TimeZoneVerifiedClinicId", "TimeZoneVerifiedByUserId") REFERENCES "Users" ("ClinicId", "Id") ON DELETE RESTRICT
);

INSERT INTO "ef_temp_Clinics" ("Id", "AddressLine", "CountryCode", "Name", "Phone", "ProvinceOrCapitalCode", "SoumOrDistrictCode", "TimeZoneId", "TimeZoneStatus", "TimeZoneVerifiedAtUtc", "TimeZoneVerifiedByUserId", "TimeZoneVerifiedClinicId", "Version", "WorkHours")
SELECT "Id", "AddressLine", "CountryCode", "Name", "Phone", "ProvinceOrCapitalCode", "SoumOrDistrictCode", "TimeZoneId", "TimeZoneStatus", "TimeZoneVerifiedAtUtc", "TimeZoneVerifiedByUserId", "TimeZoneVerifiedClinicId", "Version", "WorkHours"
FROM "Clinics";

COMMIT;

PRAGMA foreign_keys = 0;

BEGIN TRANSACTION;
DROP TABLE "Clinics";

ALTER TABLE "ef_temp_Clinics" RENAME TO "Clinics";

COMMIT;

PRAGMA foreign_keys = 1;

BEGIN TRANSACTION;
CREATE INDEX "IX_Clinics_TimeZoneVerifiedClinicId_TimeZoneVerifiedByUserId" ON "Clinics" ("TimeZoneVerifiedClinicId", "TimeZoneVerifiedByUserId");

COMMIT;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261026124849_AddClinicLocationVerification', '10.0.13');
