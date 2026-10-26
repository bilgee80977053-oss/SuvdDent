BEGIN TRANSACTION;
ALTER TABLE "Clinics" ADD "TimeZoneId" TEXT NOT NULL DEFAULT 'Asia/Ulaanbaatar';

CREATE TABLE "DoctorServices" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_DoctorServices" PRIMARY KEY AUTOINCREMENT,
    "ClinicId" INTEGER NOT NULL,
    "DoctorProfileId" INTEGER NOT NULL,
    "ServiceId" INTEGER NOT NULL,
    "DurationMinutes" INTEGER NOT NULL,
    "BufferBeforeMinutes" INTEGER NOT NULL,
    "BufferAfterMinutes" INTEGER NOT NULL,
    "IsActive" INTEGER NOT NULL,
    CONSTRAINT "CK_DoctorServices_Duration" CHECK ("DurationMinutes" > 0 AND "BufferBeforeMinutes" >= 0 AND "BufferAfterMinutes" >= 0 AND "DurationMinutes" + "BufferBeforeMinutes" + "BufferAfterMinutes" <= 1440),
    CONSTRAINT "FK_DoctorServices_DoctorProfiles_ClinicId_DoctorProfileId" FOREIGN KEY ("ClinicId", "DoctorProfileId") REFERENCES "DoctorProfiles" ("ClinicId", "Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_DoctorServices_Services_ClinicId_ServiceId" FOREIGN KEY ("ClinicId", "ServiceId") REFERENCES "Services" ("ClinicId", "Id") ON DELETE RESTRICT
);

CREATE TABLE "PatientAvailabilitySchedules" (
    "ClinicId" INTEGER NOT NULL,
    "PatientId" INTEGER NOT NULL,
    "Version" TEXT NOT NULL,
    CONSTRAINT "PK_PatientAvailabilitySchedules" PRIMARY KEY ("ClinicId", "PatientId"),
    CONSTRAINT "FK_PatientAvailabilitySchedules_Patients_ClinicId_PatientId" FOREIGN KEY ("ClinicId", "PatientId") REFERENCES "Patients" ("ClinicId", "Id") ON DELETE RESTRICT
);

CREATE TABLE "PatientAvailabilities" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_PatientAvailabilities" PRIMARY KEY AUTOINCREMENT,
    "ClinicId" INTEGER NOT NULL,
    "PatientId" INTEGER NOT NULL,
    "StartUtc" INTEGER NOT NULL,
    "EndUtc" INTEGER NOT NULL,
    CONSTRAINT "CK_PatientAvailabilities_Interval" CHECK ("StartUtc" >= 0 AND "StartUtc" < "EndUtc" AND "EndUtc" <= 3155378975999999999),
    CONSTRAINT "FK_PatientAvailabilities_PatientAvailabilitySchedules_ClinicId_PatientId" FOREIGN KEY ("ClinicId", "PatientId") REFERENCES "PatientAvailabilitySchedules" ("ClinicId", "PatientId") ON DELETE RESTRICT
);

CREATE UNIQUE INDEX "IX_DoctorServices_ClinicId_DoctorProfileId_ServiceId" ON "DoctorServices" ("ClinicId", "DoctorProfileId", "ServiceId");

CREATE INDEX "IX_DoctorServices_ClinicId_ServiceId" ON "DoctorServices" ("ClinicId", "ServiceId");

CREATE UNIQUE INDEX "IX_PatientAvailabilities_ClinicId_PatientId_StartUtc" ON "PatientAvailabilities" ("ClinicId", "PatientId", "StartUtc");

CREATE TABLE "ef_temp_Services" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Services" PRIMARY KEY AUTOINCREMENT,
    "AftercareText" TEXT NULL,
    "ClinicId" INTEGER NOT NULL,
    "Description" TEXT NOT NULL,
    "ImagePath" TEXT NULL,
    "IsActive" INTEGER NOT NULL,
    "MaxPrice" INTEGER NULL,
    "MinPrice" INTEGER NOT NULL,
    "Name" TEXT NOT NULL,
    CONSTRAINT "AK_Services_ClinicId_Id" UNIQUE ("ClinicId", "Id")
);

INSERT INTO "ef_temp_Services" ("Id", "AftercareText", "ClinicId", "Description", "ImagePath", "IsActive", "MaxPrice", "MinPrice", "Name")
SELECT "Id", "AftercareText", "ClinicId", "Description", "ImagePath", "IsActive", "MaxPrice", "MinPrice", "Name"
FROM "Services";

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
    CONSTRAINT "AK_Patients_ClinicId_Id" UNIQUE ("ClinicId", "Id"),
    CONSTRAINT "FK_Patients_Users_ClinicId_UserId" FOREIGN KEY ("ClinicId", "UserId") REFERENCES "Users" ("ClinicId", "Id") ON DELETE RESTRICT
);

INSERT INTO "ef_temp_Patients" ("Id", "Address", "Allergies", "ClinicId", "CreatedAt", "DateOfBirth", "FullName", "IsActive", "MedicalNotes", "Phone", "UserId")
SELECT "Id", "Address", "Allergies", "ClinicId", "CreatedAt", "DateOfBirth", "FullName", "IsActive", "MedicalNotes", "Phone", "UserId"
FROM "Patients";

COMMIT;

PRAGMA foreign_keys = 0;

BEGIN TRANSACTION;
DROP TABLE "Services";

ALTER TABLE "ef_temp_Services" RENAME TO "Services";

DROP TABLE "Patients";

ALTER TABLE "ef_temp_Patients" RENAME TO "Patients";

COMMIT;

PRAGMA foreign_keys = 1;

BEGIN TRANSACTION;
CREATE UNIQUE INDEX "IX_Patients_ClinicId_UserId" ON "Patients" ("ClinicId", "UserId") WHERE "UserId" IS NOT NULL;

CREATE INDEX "IX_Patients_UserId" ON "Patients" ("UserId");

COMMIT;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261026120838_AddSchedulingFoundations', '10.0.13');
