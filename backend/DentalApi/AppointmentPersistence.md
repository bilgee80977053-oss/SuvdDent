# Appointment persistence and integrity

This document records the appointment storage, tenant relationship, and database
integrity guarantees used by the [patient booking API](AppointmentApi.md).

## Model and tenant boundaries

`Appointment` records clinic, patient, doctor profile, service, and the assigned
doctor-service association. Composite foreign keys include `ClinicId` for every
tenant-owned reference. All are restrictive: referenced records cannot be
physically removed. The appointment-to-doctor-service foreign key requires an
assignment for the same clinic, doctor, and service. It intentionally does not
require `IsActive`, so deactivating a service assignment does not orphan or
rewrite historical appointments. `SchedulingService.WriteServices` deactivates
omitted assignments; a referenced assignment cannot be hard-deleted.

Treatment uses `[StartUtc, EndUtc)` and occupied time uses
`[OccupiedStartUtc, OccupiedEndUtc)`. Duration and before/after buffer snapshots
must exactly match these intervals. Duration is 1-1440 minutes, buffers are
nonnegative, and total occupied time cannot exceed 1440 minutes. The clinic
timezone snapshot and interval/configuration snapshots are immutable booking
history; later clinic or doctor-service edits do not rewrite them.

Statuses are `Confirmed` and `Cancelled`. A nonempty GUID `RequestId` is unique
per clinic and patient. SQLite stores UTC .NET ticks as integers; PostgreSQL uses
`timestamp with time zone`, normalized to UTC by EF converters.

## Database overlap protection

Both providers enforce non-overlap independently of application checks:

- PostgreSQL uses partial GiST exclusion constraints for confirmed doctor
  occupied-time intervals and patient treatment-time intervals. The
  `btree_gist` extension supports equality comparisons in the composite keys.
- SQLite uses insert and update triggers with the same clinic-scoped identities
  and half-open interval comparisons. Its normal write transaction serializes
  competing writers.

Only `Confirmed` rows block time. `Cancelled` appointments do not; adjacent
intervals are permitted. Expected PostgreSQL exclusion violations (`23P01`) and
SQLite trigger conflicts (`1811`) map to HTTP 409. PostgreSQL booking continues
to use Serializable transactions; the constraints also protect against writers
that do not follow the booking service's predicate reads.

## Additive migrations and legacy-data checks

- SQLite relationship/preflight migration:
  `20261126070328_EnforceAppointmentIntegrity`
- SQLite overlap-trigger migration:
  `20261126073027_ProtectAppointmentOverlap`
- PostgreSQL relationship, preflight, and exclusion migration:
  `20261126070352_EnforceAppointmentIntegrity`

Before adding the relationship and overlap guards, each provider migration
checks for appointments without a matching doctor-service assignment or with
existing confirmed interval overlaps. It fails without deleting or rewriting
appointments if incompatible records exist; investigate and resolve them through
an explicitly reviewed data plan before considering deployment. The SQLite
migration retains the existing unique parent index while rebuilding
`Appointments`; the index is a valid SQLite parent key for the composite
foreign key. SQLite triggers are installed in the follow-on migration, after the
table rebuild, so the rebuild cannot discard them.

The PostgreSQL migration adds `btree_gist` with `IF NOT EXISTS` and does not
drop that extension on `Down`, because it may predate this migration or serve
other objects. Do not apply a `Down` migration as a data-preserving rollback.
SQLite SQL must NEVER be applied to PostgreSQL.

No persistent database migration or live PostgreSQL test has been authorized
for this work. Review generated SQL and obtain the appropriate operator
approval before any database deployment.

## Reschedule ledger

- SQLite: `20261127122653_AddAppointmentReschedules`
- PostgreSQL: `20261127122656_AddPostgreSqlAppointmentReschedules`

The migrations only add `AppointmentReschedules`. Each source has at most
one direct replacement, and a replacement belongs to at most one direct
source in its clinic. Both references use restrictive clinic-scoped foreign
keys. The service verifies patient ownership and links only the same patient.
Existing appointment rows/triggers/exclusions are not rebuilt or rewritten.
SQLite in-memory upgrade and rollback tests compare original table rows and
schema. A live database rollback that removes the ledger would lose move replay
history; do not use these Down migrations as a data-preserving rollback.

The PostgreSQL runner treats the seventh migration as a separate approval,
not covered by the tokens for the first six. This migration and live
PostgreSQL rescheduling remain unverified on a server.

## Offline validation

`AppointmentPersistenceTests` use disposable in-memory SQLite databases to
exercise scoped assignment references, historical-row preflight, trigger
overlaps, touching/cancelled/cross-clinic cases, service deactivation, and
request-key behavior. `AppointmentMigrationMetadataTests` inspect both
provider-specific migrations and snapshots and generate PostgreSQL SQL without
connecting. These checks do not establish behavior on a live PostgreSQL server.
