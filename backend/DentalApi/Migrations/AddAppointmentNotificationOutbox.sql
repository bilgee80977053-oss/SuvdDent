BEGIN TRANSACTION;
CREATE UNIQUE INDEX "AK_Appointments_ClinicId_PlatformAccountId_Id" ON "Appointments" ("ClinicId", "PlatformAccountId", "Id");

CREATE TABLE "AppointmentNotificationOutbox" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AppointmentNotificationOutbox" PRIMARY KEY,
    "AppointmentId" INTEGER NOT NULL,
    "ClinicId" INTEGER NOT NULL,
    "PlatformAccountId" INTEGER NOT NULL,
    "Channel" TEXT NOT NULL,
    "Mode" TEXT NOT NULL,
    "TemplateVersion" TEXT NOT NULL,
    "Subject" TEXT NULL,
    "Body" TEXT NULL,
    "Status" TEXT NOT NULL,
    "AttemptCount" INTEGER NOT NULL,
    "NextAttemptAtUtc" TEXT NULL,
    "LeaseToken" TEXT NULL,
    "LeaseExpiresAtUtc" TEXT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "ProcessedAtUtc" TEXT NULL,
    "LastErrorCode" TEXT NULL,
    CONSTRAINT "CK_AppointmentNotificationOutbox_State" CHECK ("Mode" = 'Demo' AND "Channel" IN ('Email','Sms') AND "Status" IN ('Pending','Processing','DemoGenerated','Failed') AND "AttemptCount" BETWEEN 0 AND 3 AND (("Status" = 'Processing' AND "LeaseToken" IS NOT NULL AND "LeaseExpiresAtUtc" IS NOT NULL) OR ("Status" <> 'Processing' AND "LeaseToken" IS NULL AND "LeaseExpiresAtUtc" IS NULL))),
    CONSTRAINT "FK_AppointmentNotificationOutbox_Appointments_ClinicId_PlatformAccountId_AppointmentId" FOREIGN KEY ("ClinicId", "PlatformAccountId", "AppointmentId") REFERENCES "Appointments" ("ClinicId", "PlatformAccountId", "Id") ON DELETE RESTRICT
);

CREATE UNIQUE INDEX "IX_AppointmentNotificationOutbox_AppointmentId_Channel" ON "AppointmentNotificationOutbox" ("AppointmentId", "Channel");

CREATE INDEX "IX_AppointmentNotificationOutbox_ClinicId_PlatformAccountId_AppointmentId" ON "AppointmentNotificationOutbox" ("ClinicId", "PlatformAccountId", "AppointmentId");

CREATE INDEX "IX_AppointmentNotificationOutbox_CreatedAtUtc" ON "AppointmentNotificationOutbox" ("CreatedAtUtc");

CREATE INDEX "IX_AppointmentNotificationOutbox_Status_NextAttemptAtUtc" ON "AppointmentNotificationOutbox" ("Status", "NextAttemptAtUtc");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261108120446_AddAppointmentNotificationOutbox', '10.0.13');

COMMIT;

