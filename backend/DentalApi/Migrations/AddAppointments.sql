BEGIN TRANSACTION;
CREATE TABLE "Appointments" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Appointments" PRIMARY KEY AUTOINCREMENT,
    "ClinicId" INTEGER NOT NULL,
    "DoctorProfileId" INTEGER NOT NULL,
    "ServiceId" INTEGER NOT NULL,
    "PatientId" INTEGER NOT NULL,
    "PlatformAccountId" INTEGER NOT NULL,
    "Status" TEXT NOT NULL,
    "StartUtc" INTEGER NOT NULL,
    "EndUtc" INTEGER NOT NULL,
    "OccupiedStartUtc" INTEGER NOT NULL,
    "OccupiedEndUtc" INTEGER NOT NULL,
    "DurationMinutesSnapshot" INTEGER NOT NULL,
    "BufferBeforeMinutesSnapshot" INTEGER NOT NULL,
    "BufferAfterMinutesSnapshot" INTEGER NOT NULL,
    "TimeZoneIdSnapshot" TEXT NOT NULL,
    "DoctorNameSnapshot" TEXT NOT NULL,
    "ServiceNameSnapshot" TEXT NOT NULL,
    "MinPriceSnapshot" INTEGER NOT NULL,
    "MaxPriceSnapshot" INTEGER NULL,
    "ClinicVersionSnapshot" TEXT NOT NULL,
    "DoctorVersionSnapshot" TEXT NOT NULL,
    "IdempotencyKey" TEXT NOT NULL,
    "RequestHash" TEXT NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "PatientCreationConsentAtUtc" TEXT NULL,
    "PatientCreationConsentVersion" TEXT NULL,
    CONSTRAINT "CK_Appointments_Snapshot" CHECK ("Status" IN ('Confirmed','Cancelled') AND "StartUtc" >= 0 AND "StartUtc" < "EndUtc" AND "OccupiedStartUtc" >= 0 AND "OccupiedStartUtc" <= "StartUtc" AND "OccupiedEndUtc" >= "EndUtc" AND "OccupiedEndUtc" <= 3155378975999999999 AND "DurationMinutesSnapshot" > 0 AND "BufferBeforeMinutesSnapshot" >= 0 AND "BufferAfterMinutesSnapshot" >= 0 AND "DurationMinutesSnapshot" + "BufferBeforeMinutesSnapshot" + "BufferAfterMinutesSnapshot" <= 1440 AND "EndUtc" - "StartUtc" = "DurationMinutesSnapshot" * 600000000 AND "StartUtc" - "OccupiedStartUtc" = "BufferBeforeMinutesSnapshot" * 600000000 AND "OccupiedEndUtc" - "EndUtc" = "BufferAfterMinutesSnapshot" * 600000000 AND "TimeZoneIdSnapshot" IN ('Asia/Ulaanbaatar','Asia/Hovd')),
    CONSTRAINT "FK_Appointments_ClinicPatientLinks_ClinicId_PatientId_PlatformAccountId" FOREIGN KEY ("ClinicId", "PatientId", "PlatformAccountId") REFERENCES "ClinicPatientLinks" ("ClinicId", "PatientId", "PlatformAccountId") ON DELETE RESTRICT,
    CONSTRAINT "FK_Appointments_DoctorProfiles_ClinicId_DoctorProfileId" FOREIGN KEY ("ClinicId", "DoctorProfileId") REFERENCES "DoctorProfiles" ("ClinicId", "Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_Appointments_Services_ClinicId_ServiceId" FOREIGN KEY ("ClinicId", "ServiceId") REFERENCES "Services" ("ClinicId", "Id") ON DELETE RESTRICT
);

CREATE INDEX "IX_Appointments_ClinicId_DoctorProfileId_Status_OccupiedStartUtc" ON "Appointments" ("ClinicId", "DoctorProfileId", "Status", "OccupiedStartUtc");

CREATE INDEX "IX_Appointments_ClinicId_PatientId_PlatformAccountId" ON "Appointments" ("ClinicId", "PatientId", "PlatformAccountId");

CREATE INDEX "IX_Appointments_ClinicId_ServiceId" ON "Appointments" ("ClinicId", "ServiceId");

CREATE UNIQUE INDEX "IX_Appointments_PlatformAccountId_IdempotencyKey" ON "Appointments" ("PlatformAccountId", "IdempotencyKey");

CREATE INDEX "IX_Appointments_PlatformAccountId_Status_StartUtc" ON "Appointments" ("PlatformAccountId", "Status", "StartUtc");

CREATE TRIGGER TR_Appointments_NoOverlap_INSERT
BEFORE INSERT ON Appointments
WHEN NEW.Status = 'Confirmed'
BEGIN
  SELECT RAISE(ABORT, 'APPOINTMENT_OVERLAP') WHERE EXISTS (
    SELECT 1 FROM Appointments a WHERE a.Id <> NEW.Id AND a.Status = 'Confirmed' AND
    ((a.ClinicId = NEW.ClinicId AND a.DoctorProfileId = NEW.DoctorProfileId AND
      a.OccupiedStartUtc < NEW.OccupiedEndUtc AND NEW.OccupiedStartUtc < a.OccupiedEndUtc) OR
     (a.PlatformAccountId = NEW.PlatformAccountId AND a.StartUtc < NEW.EndUtc AND NEW.StartUtc < a.EndUtc))
  );
END;

CREATE TRIGGER TR_Appointments_NoOverlap_UPDATE
BEFORE UPDATE ON Appointments
WHEN NEW.Status = 'Confirmed'
BEGIN
  SELECT RAISE(ABORT, 'APPOINTMENT_OVERLAP') WHERE EXISTS (
    SELECT 1 FROM Appointments a WHERE a.Id <> NEW.Id AND a.Status = 'Confirmed' AND
    ((a.ClinicId = NEW.ClinicId AND a.DoctorProfileId = NEW.DoctorProfileId AND
      a.OccupiedStartUtc < NEW.OccupiedEndUtc AND NEW.OccupiedStartUtc < a.OccupiedEndUtc) OR
     (a.PlatformAccountId = NEW.PlatformAccountId AND a.StartUtc < NEW.EndUtc AND NEW.StartUtc < a.EndUtc))
  );
END;

CREATE TABLE "ef_temp_ClinicPatientLinks" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_ClinicPatientLinks" PRIMARY KEY AUTOINCREMENT,
    "ClinicId" INTEGER NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "PatientId" INTEGER NOT NULL,
    "PlatformAccountId" INTEGER NOT NULL,
    CONSTRAINT "AK_ClinicPatientLinks_ClinicId_PatientId_PlatformAccountId" UNIQUE ("ClinicId", "PatientId", "PlatformAccountId"),
    CONSTRAINT "FK_ClinicPatientLinks_Patients_ClinicId_PatientId" FOREIGN KEY ("ClinicId", "PatientId") REFERENCES "Patients" ("ClinicId", "Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_ClinicPatientLinks_PlatformAccounts_PlatformAccountId" FOREIGN KEY ("PlatformAccountId") REFERENCES "PlatformAccounts" ("Id") ON DELETE RESTRICT
);

INSERT INTO "ef_temp_ClinicPatientLinks" ("Id", "ClinicId", "CreatedAt", "PatientId", "PlatformAccountId")
SELECT "Id", "ClinicId", "CreatedAt", "PatientId", "PlatformAccountId"
FROM "ClinicPatientLinks";

COMMIT;

PRAGMA foreign_keys = 0;

BEGIN TRANSACTION;
DROP TABLE "ClinicPatientLinks";

ALTER TABLE "ef_temp_ClinicPatientLinks" RENAME TO "ClinicPatientLinks";

COMMIT;

PRAGMA foreign_keys = 1;

BEGIN TRANSACTION;
CREATE UNIQUE INDEX "IX_ClinicPatientLinks_ClinicId_PatientId" ON "ClinicPatientLinks" ("ClinicId", "PatientId");

CREATE UNIQUE INDEX "IX_ClinicPatientLinks_PlatformAccountId_ClinicId" ON "ClinicPatientLinks" ("PlatformAccountId", "ClinicId");

COMMIT;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261026141041_AddAppointments', '10.0.13');
