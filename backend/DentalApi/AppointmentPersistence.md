# Appointment persistence foundation

This step adds storage only. Existing APIs, authentication, scheduling services,
and the Flutter demonstration are unchanged.

## Model and tenant boundaries

`Appointment` records clinic, patient, doctor profile and service IDs. Composite
foreign keys include `ClinicId` for every tenant-owned reference. All four
relationships use restrictive deletion; a referenced record cannot be removed.
No medical notes or additional patient contact data are copied into appointments.

Treatment uses `[StartUtc, EndUtc)` and occupied time uses
`[OccupiedStartUtc, OccupiedEndUtc)`. Duration and before/after buffer snapshots
must exactly match these intervals. Duration is 1-1440 minutes, buffers are
nonnegative and total occupied time cannot exceed 1440 minutes, following the
existing doctor-service configuration limits. The clinic timezone snapshot
follows the booking-history requirement in `ClinicSchedulingPolicy`; changing
clinic or doctor-service configuration does not rewrite these stored snapshots.

Statuses are `Confirmed` and `Cancelled`; these are persistence values, not new
API operations. A nonempty GUID `RequestId` is unique per clinic and patient.
That constraint supports future idempotency but does not implement replay handling.

SQLite stores UTC .NET ticks as integers, preserving ordering and 100 ns
precision. PostgreSQL uses `timestamp with time zone`, with values normalized to
UTC by EF converters; PostgreSQL storage precision is microseconds. Future booking
logic should select a common precision before deriving intervals. Database checks
validate finite/representable times, interval relationships and snapshot metadata.
Timezone identifiers still require application validation against supported zones.

Indexes support patient history and doctor occupied-time lookups. Ordinary indexes
DO NOT prevent all overlapping bookings. Booking concurrency, authorization,
verified-email enforcement, eligibility, working-hour checks, optimistic update
handling and transaction-safe confirmation delivery remain later service work.

## Separate additive migrations

- SQLite: `20261027180600_AddAppointmentFoundation`
- PostgreSQL: `20261027180725_AddPostgreSqlAppointmentFoundation`

Each creates only `Appointments`, its constraints and indexes. Existing EF
migration C# files, historical designers and SQL fixtures are preserved. Only
the two current EF model snapshots advance. Follow the
[migration workflow](Migrations/README.md) with the explicit provider context.
SQLite SQL scripts must NEVER be applied to PostgreSQL.

No migration has been applied to a real database for this step. PostgreSQL's
startup migration guard remains enabled. Review generated SQL and obtain the
appropriate operational approval before any later database deployment. The new
`Down` migrations drop `Appointments` and would delete its data; do not use them
as a data-preserving rollback.

## Offline validation

`AppointmentPersistenceTests` migrate disposable in-memory SQLite databases,
exercise tenant/check/unique constraints, verify UTC and snapshot persistence,
and compare all 13 pre-existing tables' rows and schema before/after both EF and
SQL-script upgrades. `AppointmentMigrationMetadataTests` inspect both providers'
migration operations, mappings and generated PostgreSQL SQL without connecting.
These offline checks do not establish behavior on a live PostgreSQL server.
