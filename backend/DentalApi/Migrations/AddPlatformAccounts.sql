BEGIN TRANSACTION;
CREATE TABLE "PlatformAccounts" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_PlatformAccounts" PRIMARY KEY AUTOINCREMENT,
    "FullName" TEXT NOT NULL,
    "Email" TEXT COLLATE NOCASE NULL,
    "Phone" TEXT NULL,
    "PasswordHash" TEXT NOT NULL,
    "IsActive" INTEGER NOT NULL,
    "IsEmailVerified" INTEGER NOT NULL,
    "IsPhoneVerified" INTEGER NOT NULL,
    "TokenVersion" INTEGER NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    CONSTRAINT "CK_PlatformAccounts_Contacts" CHECK (("Email" IS NOT NULL OR "Phone" IS NOT NULL) AND "TokenVersion" >= 0)
);

CREATE TABLE "ClinicPatientLinks" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_ClinicPatientLinks" PRIMARY KEY AUTOINCREMENT,
    "PlatformAccountId" INTEGER NOT NULL,
    "ClinicId" INTEGER NOT NULL,
    "PatientId" INTEGER NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    CONSTRAINT "FK_ClinicPatientLinks_Patients_ClinicId_PatientId" FOREIGN KEY ("ClinicId", "PatientId") REFERENCES "Patients" ("ClinicId", "Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_ClinicPatientLinks_PlatformAccounts_PlatformAccountId" FOREIGN KEY ("PlatformAccountId") REFERENCES "PlatformAccounts" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "PlatformVerificationCodes" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_PlatformVerificationCodes" PRIMARY KEY AUTOINCREMENT,
    "PlatformAccountId" INTEGER NOT NULL,
    "Channel" TEXT NOT NULL,
    "Purpose" TEXT NOT NULL,
    "CodeHash" TEXT NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "ExpiresAt" TEXT NOT NULL,
    "FailedAttempts" INTEGER NOT NULL,
    "IsUsed" INTEGER NOT NULL,
    CONSTRAINT "FK_PlatformVerificationCodes_PlatformAccounts_PlatformAccountId" FOREIGN KEY ("PlatformAccountId") REFERENCES "PlatformAccounts" ("Id") ON DELETE RESTRICT
);

CREATE UNIQUE INDEX "IX_ClinicPatientLinks_ClinicId_PatientId" ON "ClinicPatientLinks" ("ClinicId", "PatientId");

CREATE UNIQUE INDEX "IX_ClinicPatientLinks_PlatformAccountId_ClinicId" ON "ClinicPatientLinks" ("PlatformAccountId", "ClinicId");

CREATE UNIQUE INDEX "IX_PlatformAccounts_Email" ON "PlatformAccounts" ("Email") WHERE "Email" IS NOT NULL;

CREATE UNIQUE INDEX "IX_PlatformAccounts_Phone" ON "PlatformAccounts" ("Phone") WHERE "Phone" IS NOT NULL;

CREATE INDEX "IX_PlatformVerificationCodes_PlatformAccountId_Channel_Purpose_CreatedAt" ON "PlatformVerificationCodes" ("PlatformAccountId", "Channel", "Purpose", "CreatedAt");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261026131551_AddPlatformAccounts', '10.0.13');

COMMIT;
