# Appointment confirmations: DEMO only

Every NEW successful booking records exactly one Email and one Sms intent in
AppointmentNotificationOutbox in the SAME SQLite transaction as Appointment,
Patient and ClinicPatientLink. Recording failure aborts that transaction. Provider
failures happen later and cannot undo the committed appointment. An exact
Idempotency-Key replay returns before recording, and UNIQUE(AppointmentId,Channel)
provides a database guard. Existing bookings are not backfilled on migration/replay.

The internal IAppointmentConfirmationNotifications boundary records intents;
IDemoAppointmentNotificationProvider has no destination/SMTP/SMS dependency.
Messages explicitly say DEMO/no email or SMS sent. The Email includes NX-{id},
doctor/service snapshot names and appointment date/time converted through the
stored IANA clinic timezone, including its UTC offset. SMS contains a short
reference/time/timezone. Neither channel represents delivery. Accounts without
email or phone still get both simulated intents; no contacts are read or stored.
MailKit EmailService and the separate authentication message sender are unchanged.

## Processing and privacy

AppointmentNotificationProcessor.ProcessBatchAsync processes at most 20 intents,
using an atomic conditional update and a two-minute lease to coordinate workers.
Provider invocation is outside any booking transaction. Failures use only the fixed
DEMO_PROVIDER_FAILURE code, retry after one minute and stop after three attempts.
Expired leases can be reclaimed. A crash on the third attempt becomes Failed.
A crash between provider completion and status update can repeat a simulation;
this is at-least-once processing, not a real-delivery exactly-once guarantee.

The background worker is OFF unless DemoNotifications:Enabled=true is explicitly
configured, and is always off in Testing. When enabled it ticks every ten seconds.
Tests invoke the processor directly. This change does not run a standalone worker,
start the API, or apply migrations to an existing file database. Enable only in a
reviewed, isolated DEMO environment after separately approved DB provisioning.

Content is private DB data, not public preview data. There is no notification HTTP
endpoint and no change to booking DTOs, JWT policies or patient-record access.
The composite FK binds the outbox's clinic/account/appointment to one appointment;
unauthorized IDs cannot reassign rows. The worker is a trusted internal batch
processor, not a user-scoped query API. Do not enable EF sensitive-data logging,
SQL parameter logging, or dump DB/message content into logs or reports.
Provider exceptions and message bodies are never logged by these services.

Retention is fixed at 24 hours from intent creation. CleanupAsync erases Subject
and Body for expired rows, including Pending/Processing/Failed rows, and expires
unfinished work. DemoGenerated remains a simulation status. ID/channel/ownership
metadata is retained as a deduplication tombstone; cleanup never deletes bookings.
Cleanup runs before each processing batch. If the worker is disabled/offline,
cleanup must be invoked by a trusted maintenance host when it resumes; this is not
a wall-clock deletion guarantee while the service is stopped. Existing database
backups are outside this cleanup and need separate access/retention controls.

## Schema review and safety

The additive AddAppointmentNotificationOutbox migration creates the outbox,
state/lease CHECK, unique booking/channel index, queue/creation-time indexes and
Restrict composite FK. A unique parent index on
Appointments(ClinicId,PlatformAccountId,Id) implements EF's alternate key without
rebuilding Appointments. No historical migration is edited and neither overlap
trigger nor appointment CHECK/FK is replaced. SQLite accepts the unique index as
the parent FK key; future migrations must retain this custom index implementation.
The SQL artifact is AddAppointmentNotificationOutbox.sql. Up has no DROP/rebuild or
backfill. Down drops notification data; it is not a recovery or deployment command.
Normal non-Testing startup still auto-migrates; do not start against a persistent
DB without separate deployment approval.

Tests rehearse EF and the exact SQL on in-memory SQLite with an existing booking,
compare old data/schema/sequences/triggers, exercise FK/CHECK/overlap enforcement,
and verify atomicity, idempotency, failures, leases, account/clinic boundaries,
UTC offsets and retention. No real Email/SMS or persistent database is used.

Flutter notification endpoints/UI and production transports are outside this phase.
