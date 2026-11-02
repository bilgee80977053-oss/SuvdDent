# STEP 7.2A — Email and SMS Notification Outbox

Date: 2026-11-03 (Asia/Ulaanbaatar)
Branch: feature/school-appointment-mvp
Base commit: 518bd3d

## Implemented features

- Booking confirmation now records Email, SMS, and a scheduled SMS reminder in the same database transaction as the appointment. Request replay and booking conflict do not create additional records. Transport calls no longer run inside the HTTP booking operation.
- Tenant-scoped AppointmentService.Cancel validates ownership and rejects appointments that have started. It commits cancellation and its Email/SMS outbox records atomically. Missing/foreign appointments, rejected cancellation, and failed commits produce no cancellation records. Repeated cancellation returns the existing result without requeueing.
- Cancellation is an application service method only. Existing controller routes, request DTOs, response DTOs, and authentication contracts remain unchanged.
- SMS reminders are scheduled at StartUtc minus 24 hours. Bookings made less than 24 hours before the appointment do not create a late 24-hour reminder. The worker suppresses reminders for cancelled appointments or appointments that have already started.
- Confirmation and cancellation use the requested Mongolian text, followed by the appointment's local date/time and stored clinic timezone. Reminder messages also include the local date/time.
- NotificationPreference stores separate EmailEnabled and SmsEnabled consent for each clinic/user. Missing preferences mean no consent; both flags default to false. Queueing requires the selected contact to be nonempty and verified. Delivery rechecks consent, active patient/user status, patient role, verification, and exact contact equality. Changed contacts are suppressed rather than silently redirected.
- Composite foreign keys preserve tenant boundaries for preferences and appointment notifications. A unique (ClinicId, AppointmentId, Event, Channel) index is the final duplicate-record guard.
- NotificationWorker is a BackgroundService with a fresh DI scope per attempt. It polls every five seconds when idle. It is disabled unless explicitly configured and always exits in Testing.
- Processing uses SQLite's write transaction or PostgreSQL ReadCommitted with FOR UPDATE SKIP LOCKED on outbox records and FOR UPDATE on the appointment. Cancellation locks that same appointment. Locks remain held until the delivery result is persisted.
- Each failed transport attempt uses durable exponential backoff: 1, 2, 4, 8, 16, 32, then 60 minutes. The eighth failure marks the row Failed. Email and SMS failures are independent. Database failures leave the record eligible for a later worker poll.
- Outbox IDs provide stable delivery keys. SMS sends Idempotency-Key; email uses a stable SMTP Message-ID. SMTP calls have a 15-second operation timeout and cancellation support. Failure to disconnect after SMTP acceptance does not cause a retry.
- Provider exceptions and credentials are not persisted in outbox rows. Worker infrastructure logs contain exception type only.

## Transport configuration and consent

All settings are disabled by default; example configuration contains no usable credentials.

- Set Notifications:WorkerEnabled to true only after migration rollout and transport configuration are authorized.
- Email continues to require AppointmentEmail:Enabled=true and existing Email settings.
- SMS requires Notifications:Sms:Enabled=true and an HTTPS Notifications:Sms:Endpoint.
- Optional gateway token comes from Notifications__Sms__Token in the environment or another secret configuration provider.
- SMS gateway contract: HTTPS POST JSON containing recipient and message; Idempotency-Key header identifies the outbox record; optional Bearer authorization. A 2xx response must mean durable acceptance. The gateway must deduplicate the key.
- Consent must be explicitly provisioned in NotificationPreference for the verified clinic user. No preference-management API or automatic consent backfill was added.
- Existing STEP 7.1 direct-email sender types remain for source compatibility but are no longer registered or called by appointment booking.

## Migrations

New migrations only:

- SQLite: 20261102165207_AddNotificationOutbox
- PostgreSQL: 20261102165221_AddPostgreSqlNotificationOutbox

Both add NotificationOutbox and NotificationPreference, the appointment composite alternate key (ClinicId, Id), tenant foreign keys, uniqueness/due-work indexes, and state/channel/event/attempt constraints. Outbox schedule timestamps use UTC ticks: INTEGER on SQLite and bigint on PostgreSQL.

SQLite must rebuild Appointments to add the composite alternate key. The migration test upgrades a populated disposable in-memory database, compares complete appointment and user values, and checks integrity and foreign keys. The upgrade does not intentionally drop existing data or columns. PostgreSQL uses an additive unique constraint and new tables; its upgrade SQL is generated and checked offline.

Historical migration source/designer files are unchanged. Both current model snapshots were updated and are checked for pending model differences. Migration IDs were generated from UTC tooling time; the report date uses the client's local timezone.

No migration was applied to an existing database. EF generation used the existing isolated design-time factories. Tests migrate disposable SQLite in-memory databases only; PostgreSQL migration metadata and SQL tests do not open a connection.

## Validation

- dotnet build backend\NexoraDental.sln: PASSED, 0 warnings, 0 errors.
- Focused initial outbox/migration run: 61 passed, 0 failed, 0 skipped.
- Initial full run: 372 passed, 3 failed, 12 skipped. The failures were historical tests assuming exactly 11 migrations or no migrations after the appointment foundation. Their assertions were updated to compare against the intended migration boundary without weakening data-preservation checks.
- Final dotnet test backend\NexoraDental.sln: PASSED; 375 passed, 0 failed, 12 skipped, 387 total (test duration 1 m 39 s).
- git diff --check: PASSED (exit 0). Git emits LF-to-CRLF conversion notices; no whitespace errors.
- The live PostgreSQL test connection environment variable is absent. The 12 existing PostgreSQL workflow/concurrency tests skipped for this reason.
- Notification tests use FakeSmsSender, FakeNotificationEmailSender, and an in-process HTTP handler. No real SMS, email, credentials, or production database was used.

Coverage includes commit rollback, request replay/conflict, both channels, transport independence, durable retries and stable keys, exhaustion, due-time boundaries, late booking, expired reminders, cancellation success/replay/failure/foreign ownership, consent withdrawal, verification loss, changed contacts, inactive users, concurrent SQLite processors, database uniqueness, cross-tenant FK rejection, populated SQLite migration, and offline PostgreSQL migration metadata/SQL.

## Changed files

Application:

- DentalApi/Data/AppDbContext.cs
- DentalApi/Data/NotificationModelConfiguration.cs (new)
- DentalApi/Models/NotificationOutbox.cs (new)
- DentalApi/Services/AppointmentService.cs
- DentalApi/Services/EmailService.cs
- DentalApi/Services/NotificationQueue.cs (new)
- DentalApi/Services/NotificationProcessor.cs (new; includes BackgroundService)
- DentalApi/Services/NotificationSenders.cs (new)
- DentalApi/Program.cs
- DentalApi/appsettings.Example.json

Migration artifacts:

- DentalApi/Migrations/20261102165207_AddNotificationOutbox.cs (new)
- DentalApi/Migrations/20261102165207_AddNotificationOutbox.Designer.cs (new)
- DentalApi/Migrations/AppDbContextModelSnapshot.cs
- DentalApi/Migrations/PostgreSql/20261102165221_AddPostgreSqlNotificationOutbox.cs (new)
- DentalApi/Migrations/PostgreSql/20261102165221_AddPostgreSqlNotificationOutbox.Designer.cs (new)
- DentalApi/Migrations/PostgreSql/PostgreSqlAppDbContextModelSnapshot.cs

Tests and report:

- DentalApi.IntegrationTests/AppointmentApiTests.cs
- DentalApi.IntegrationTests/AppointmentPersistenceTests.cs (historical migration boundary assertion)
- DentalApi.IntegrationTests/SchoolFixtureTests.cs (current migration-set assertion)
- DentalApi.IntegrationTests/FakeNotificationSenders.cs (new)
- DentalApi.IntegrationTests/NotificationMigrationTests.cs (new)
- DentalApi.IntegrationTests/NotificationSenderTests.cs (new)
- STEP7_2A_REPORT.md (new)

## Remaining limitations

- Delivery is at least once. A crash or database failure after provider acceptance but before recording Sent can cause replay. SMTP cannot guarantee exactly-once delivery even with a stable Message-ID. SMS requires gateway-side idempotency.
- Sent means provider acceptance, not recipient delivery. There are no provider delivery receipts in this step.
- Live PostgreSQL outbox processing, locking, and migration execution were not exercised. Only its model and generated upgrade SQL were validated offline.
- Locks are held during transport I/O. This favors MVP correctness but limits SQLite write concurrency and throughput.
- Reminder timing is subject to polling, worker downtime, and retry delays. Overdue reminders are suppressed once the appointment begins.
- Consent management UI/API, a cancellation HTTP endpoint, provider-specific SMS integrations, preference/reminder backfills, retention cleanup, monitoring UI, and manual replay of Failed records remain future work.
- Existing users are not automatically opted in. Existing appointments are not backfilled with notification records.
- The worker and transport flags remain off; no production rollout was performed.

No changes to main, commits, pushes, real transport rehearsal, or migration application to existing databases were performed.

## Final git status --short

~~~text
 M backend/DentalApi.IntegrationTests/AppointmentApiTests.cs
 M backend/DentalApi.IntegrationTests/AppointmentPersistenceTests.cs
 M backend/DentalApi.IntegrationTests/SchoolFixtureTests.cs
 M backend/DentalApi/Data/AppDbContext.cs
 M backend/DentalApi/Migrations/AppDbContextModelSnapshot.cs
 M backend/DentalApi/Migrations/PostgreSql/PostgreSqlAppDbContextModelSnapshot.cs
 M backend/DentalApi/Program.cs
 M backend/DentalApi/Services/AppointmentService.cs
 M backend/DentalApi/Services/EmailService.cs
 M backend/DentalApi/appsettings.Example.json
?? backend/DentalApi.IntegrationTests/FakeNotificationSenders.cs
?? backend/DentalApi.IntegrationTests/NotificationMigrationTests.cs
?? backend/DentalApi.IntegrationTests/NotificationSenderTests.cs
?? backend/DentalApi/Data/NotificationModelConfiguration.cs
?? backend/DentalApi/Migrations/20261102165207_AddNotificationOutbox.Designer.cs
?? backend/DentalApi/Migrations/20261102165207_AddNotificationOutbox.cs
?? backend/DentalApi/Migrations/PostgreSql/20261102165221_AddPostgreSqlNotificationOutbox.Designer.cs
?? backend/DentalApi/Migrations/PostgreSql/20261102165221_AddPostgreSqlNotificationOutbox.cs
?? backend/DentalApi/Models/NotificationOutbox.cs
?? backend/DentalApi/Services/NotificationProcessor.cs
?? backend/DentalApi/Services/NotificationQueue.cs
?? backend/DentalApi/Services/NotificationSenders.cs
?? backend/STEP7_2A_REPORT.md
~~~
