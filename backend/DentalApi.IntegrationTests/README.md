# DentalApi integration tests

Run from the repository root:

    dotnet test backend/NexoraDental.sln -c Release

Release avoids replacing Debug executables that may be running in the IDE.

Uses .NET 10, xUnit, WebApplicationFactory, and the real JWT bearer middleware,
including database-backed claim validation. No fake authentication handler.

The original SQLite tests each own an isolated in-memory database. Their factory
replaces application DbContext options and checks the actual connection before
EnsureCreated. It clears application configuration and supplies synthetic JWT
settings. SQLite tests do not use a real database file or production connection string.

The Testing environment skips startup migration and seeder. Tests assert the
database is empty, then seed Clinic A/B and synthetic users and patients only.
SMTP configuration is absent. EmailService uses empty configuration: accidental
sends fail while constructing the sender address before opening a connection.
Reset codes are inserted directly into the synthetic database; no email is sent.

Coverage:
- Only the caller's patient profile is returned despite foreign query/header IDs.
- Foreign IDs and role fields in PUT cannot target Clinic B.
- B's complete stored user/patient snapshots remain unchanged.
- Unsigned JWT payload changes fail signature validation (401).
- Signed tokens whose clinic/role differs from the stored user fail (401).
- Anonymous callers get 401; authenticated callers with the wrong role get 403.
- Password reset revokes A's old JWT, accepts the new password, rejects the old
  password, and preserves B's data and existing JWT.

Scope: patient self-service isolation and authentication. Public service catalogs
and future staff CRUD require separate tests.
## Patient–User tenant integrity

TenantIntegrityTests applies real EF migrations to its own SQLite in-memory
connection with Foreign Keys=True. It upgrades from AddUserTokenVersion with
synthetic rows in every application table. It verifies:
- Same-tenant and nullable UserId links succeed.
- Cross-tenant and missing users fail with SQLite FK error 787.
- Raw SQL cannot break patient/user links or delete a referenced user.
- All row values, column types/nullability/defaults/primary keys, existing named
  indexes, and the VerificationCodes foreign key survive the rebuild.
- Foreign keys remain enabled, foreign_key_check is empty, and IDs still generate.
- Conflicting legacy rows abort the new migration before rebuilding; no automatic
  repair or deletion occurs and the migration is not recorded as applied.

The design-time AppDbContext factory intentionally uses only an in-memory
connection; schema tooling does not start the web app or use real configuration.
The generated `AddPatientUserTenantIntegrity.sql` is now preserved unchanged at
`../DentalApi/Migrations/Archive/Sqlite/AddPatientUserTenantIntegrity.sql` as a
historical review artifact. No integration test reads this file; TenantIntegrityTests
runs the source C# migration `20261026100808_AddPatientUserTenantIntegrity` directly.
The archived artifact has not been applied to a real database by this cleanup. Users and Patients are rebuilt. The preflight CHECK rejects
bad legacy links. A helper unique index supports SQLite's copy phase and is
removed when the old Users table is dropped.

Before deploying, back up and rehearse against an authorized isolated copy.
Outside `Testing`, SQLite application startup automatically applies pending
migrations. PostgreSQL startup remains blocked by its migration guard before
migration/seeding. Do not start the app against a real database as part of this
cleanup.

## Scheduling foundations

SchedulingTests uses DoctorApiFactory: real JWT authentication, independent request
connections to a uniquely named SQLite in-memory database, synthetic tenants, no
production configuration, no startup seeding and no real SMTP transport. Tests
cover role/tenant/ownership checks, duration validation, explicit timestamp offsets,
UTC normalization, overlap rejection, empty-list versions, stale/concurrent writes,
shared doctor versions and raw SQL foreign key/check/unique constraints.

SchedulingMigrationTests rehearses both the EF migration and generated SQL from
AddDoctorProfiles with synthetic rows. It checks old row values, column metadata,
named indexes, old FKs, nullable patient links, new timezone defaults, ID generation,
integrity, migration history and absence of pending model changes. Historical
TenantIntegrityTests and DoctorMigrationTests still target their original migration
boundaries; their Clinic fixtures use that historical schema without TimeZoneId.

See ../DentalApi/SchedulingApi.md for endpoints, examples and deployment risks.


## Clinic location and timezone verification

ClinicLocationTests covers all 22 province/capital entries and 339 districts/soums,
location membership, timezone suggestions, explicit Director confirmation, tenant
spoofing, roles, stale/concurrent edits, invalidation and raw SQL constraints.
The scheduling policy is exercised within a real transaction with Ulaanbaatar and
Hovd clinics, fail-closed Pending behavior and stale scheduling versions.
ClinicLocationMigrationTests applies scheduling and verification migrations in
sequence on synthetic in-memory SQLite and separately tests the generated SQL.
No real database, backups, startup seeding or SMTP transport is used.
See ../DentalApi/ClinicLocationApi.md for the future booking integration contract.


## Platform identity foundation

PlatformApiFactory derives the isolated in-memory DoctorApiFactory and adds an
independent synthetic Platform JWT configuration plus an in-memory fake email/SMS
sender. PlatformAccountTests covers verification, normalized unique contacts,
password byte limits, scheme isolation, ownership, token revocation, recovery/code
replay/expiry/attempt limits and ClinicPatientLink tenant FKs. PlatformMigrationTests
checks both EF migration and generated SQL preserve legacy rows/schema/indexes/FKs.
No public auto-link flow, real transport, real database or credential backfill exists.
See ../DentalApi/PlatformAccountApi.md for configuration and deployment limitations.


## Provider compatibility (Phase 2, Step 3)

`ProviderCompatibilityTests` uses the real Npgsql EF provider with connection
opening suppressed, then injects synthetic PostgreSQL errors before transaction
creation. It verifies every transactional service entry point chooses PostgreSQL
`RepeatableRead` for scheduling snapshots or `Serializable` for validated writes,
and returns the existing 409 response for SQL states 23505, 40001 and 40P01.
Tests cover direct exceptions, EF wrappers and Npgsql transient-error wrappers;
unrelated SQL states propagate to the existing sanitized error handler.
No PostgreSQL socket, Docker, credentials or external database is needed.

Additional failures injected at commit against an isolated SQLite in-memory
schema verify rollback of versions, availability, profile changes, location and
verification-code consumption, including unchanged Clinic B data. SQLite busy,
locked and existing unique/primary-key conflict paths retain 409 responses.
Existing integration tests continue to exercise tenant isolation, stale versions,
concurrent writes, code replay and API contracts against SQLite.

SQLite reads keep deferred transactions; writes keep their default immediate
transactions. PostgreSQL Serializable also protects predicate reads used by
first-time availability writes and Platform challenge/verification. Conflicts
require the caller to reload/retry; services never replay writes automatically.
ClinicId predicates and optimistic version comparisons are unchanged.

These are offline compatibility checks, not a PostgreSQL concurrency rehearsal.
Real PostgreSQL snapshot, deadlock and serialization behavior under competing
connections still requires a separately authorized disposable database run.
The PostgreSQL startup migration guard stays enabled; this step changes no model
or migration and applies no migrations to a real database.


## Real PostgreSQL integration (Phase 2, Step 4)

These tests cover only existing patients, doctor profiles, working hours,
doctor service assignments and patient availability. There is no appointment
model or booking endpoint, and these tests do not claim booking coverage.

Opt in by configuring `NEXORA_PG_TEST_CONNECTION` in the operating system
environment inherited by the test process. Obtain its value privately; do not
paste it into shell history, source, appsettings, documentation or test output.
The test suite never reads a production/default connection string as fallback
and never changes process environment variables. Restart the runner if necessary
so it inherits a newly configured variable.

Without the variable, all ten real PostgreSQL tests are **skipped / not executed**;
SQLite, offline provider and target-safety tests still run normally.
An invalid, nonempty variable fails preflight rather than silently skipping.

```powershell
dotnet build backend/NexoraDental.sln
dotnet test backend/NexoraDental.sln
# Requires the explicit environment variable, already configured privately:
dotnet test backend/NexoraDental.sln --filter "Category=PostgreSqlIntegration|Category=PostgreSqlConcurrency"
git diff --check
```

Before any fixture writes, the guard validates exactly host `127.0.0.1`, port
`5432`, database `nexora_dental_test`, and user `nexora_dev`. Alternate search paths
and startup options are rejected. It then opens a read-only preflight connection,
checks `current_database()`, `current_user`, PostgreSQL major version 18 and the
`20261027132306_PostgreSqlBaseline` row in the public EF migrations history.
Preflight does not change transaction/server settings or schema. Connection
pooling, ambient enlistment, parameter logging and detailed errors are disabled.
Parser, preflight, setup and cleanup failures do not include connection values.

`PostgreSqlApiFactory` runs WebApplicationFactory in `Testing`, replaces database
registrations with the real `PostgreSqlAppDbContext`, and uses synthetic signed
JWTs with the existing bearer middleware and database-backed claim validation.
It retains the original dummy Testing configuration for startup validation;
Npgsql receives only the validated opt-in connection directly through DI.
Production settings and the PostgreSQL startup migration guard are unchanged.
Logging providers and SMTP transport are disabled in this test host.

Each test inserts two clinics with generated IDs and a unique `pg-step4-<GUID>`
marker, plus synthetic users, services and one marked platform account. It never
assumes that an outer transaction isolates HTTP requests. Teardown revalidates
the target and ownership, then deletes only those clinics' dependent rows and
the exact owned platform account in foreign-key order inside one transaction.
Cleanup verifies the owned roots are gone and fails the test if cleanup fails.
It does not delete rows from previous runs, use fixed IDs, reset identity sequences,
apply migrations, drop databases/schemas, truncate tables or reset the schema.
Constraint probes always roll back, even if an expected rejection fails.

Normal completion cleans up committed synthetic rows. An interrupted process,
lost connection or changed ownership can leave marked test data. Cleanup errors
identify only the run marker; inspect that run explicitly before any manual
cleanup. Do not perform a broad marker-based purge or reset. Identity sequences
advance normally and are intentionally not rewound.

Coverage is separated by class/category:

- Existing SQLite regression suite: original tests and factories, unchanged.
- Offline provider compatibility: `ProviderCompatibilityTests` and
  `PostgreSqlBaselineTests`; no PostgreSQL connection.
- Offline target safety: `PostgreSqlTargetSafetyTests`; invalid targets and
  malformed inputs are rejected without opening connections.
- Real workflows: `PostgreSqlWorkflowTests`, category `PostgreSqlIntegration`;
  patient CRUD/calendar DOB, doctor creation/profile/hours/version checks,
  real JWT roles/claim mismatch, service validation, availability replacement,
  UTC precision, cross-clinic isolation, real FK/unique/CHECK constraints,
  case-insensitive platform email equality/uniqueness, and rollback.
- Real concurrency: `PostgreSqlConcurrencyTests`, category
  `PostgreSqlConcurrency`; independent contexts and physical connections.
  Deterministic query barriers put both writers on the same version before
  competing updates. Tests require one winner, one safe 409, a genuine 40001
  serialization failure and no loser data. A paused scheduling read verifies
  its RepeatableRead version/list snapshot survives a competing committed write.

Compilation and skipped results do not establish real PostgreSQL behavior.
Run both opt-in categories with the authorized target before accepting this
step as database-validated. The harness bounds query-barrier waits and SQL command
execution; overloaded machines may need a separate timing investigation if a
concurrency test times out.


## Historical SQL artifacts and offline cleanup validation (Phase 2, Step 5)

See the [EF Core migration workflow](../DentalApi/Migrations/README.md) for
provider-specific `--context` usage, migration creation and SQL generation.
**SQLite SQL scripts must NEVER be applied to PostgreSQL.**

Only the unreferenced tenant integrity review script was archived; its bytes are
unchanged. Existing C# migrations, designers and both snapshots remain unchanged.
The following SQLite scripts are active test fixtures and retain their paths:

| File in `../DentalApi/Migrations/` | Consumer |
| --- | --- |
| `AddSchedulingFoundations.sql` | `SchedulingMigrationTests` |
| `AddClinicLocationVerification.sql` | `ClinicLocationMigrationTests` |
| `AddPlatformAccounts.sql` | `PlatformMigrationTests` |

These tests compare the file to EF-generated SQL for its exact historical range
and execute it only in an isolated SQLite in-memory database. No test-path changes
are required. Do not regenerate the fixtures from the current model or overwrite
them with PostgreSQL output.

For this cleanup, require the opt-in environment variable to be absent. This
PowerShell check prints no connection values and stops if it is configured:

```powershell
if (Test-Path Env:NEXORA_PG_TEST_CONNECTION) { throw 'Remove the PostgreSQL test opt-in before offline validation.' }
dotnet build backend/NexoraDental.sln
dotnet test backend/NexoraDental.sln
git diff --check
```

Expect all ten real PostgreSQL tests to be skipped, not passed. The remaining
suite uses in-memory SQLite or offline checks. Stop and investigate if any
historical migration test fails; do not edit migration history or SQL contents
to bypass the failure. This validation must not connect to an existing database.


## Appointment persistence foundation (University MVP, Step 2)

`AppointmentPersistenceTests` use only disposable in-memory SQLite databases.
They cover additive migration and generated SQL upgrades, preservation of all
13 historical tables, composite tenant foreign keys, invalid intervals and
snapshots, UTC ordering, request-key uniqueness and half-open boundaries.
`AppointmentMigrationMetadataTests` inspect provider-specific migrations and
PostgreSQL SQL offline; they never open a PostgreSQL connection.

The baseline regression test selects its exact historical migration and target
model. The platform migration test checks the expected later pending migrations
when stopped at its historical target. Their historical schema/data and SQL
fixture assertions remain in place.

For offline validation, require `NEXORA_PG_TEST_CONNECTION` to be absent using the
check above. All ten live PostgreSQL tests should skip. See the
[appointment storage scope](../DentalApi/AppointmentPersistence.md) for schema
semantics, precision differences and booking behavior reserved for later steps.
