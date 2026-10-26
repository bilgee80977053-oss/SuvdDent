# DentalApi integration tests

Run from the repository root:

    dotnet test backend/NexoraDental.sln -c Release

Release avoids replacing Debug executables that may be running in the IDE.

Uses .NET 10, xUnit, WebApplicationFactory, and the real JWT bearer middleware,
including database-backed claim validation. No fake authentication handler.

Each test owns a separate SQLite Data Source=:memory: connection. The factory
replaces application DbContext options and checks the actual connection before
EnsureCreated. It clears application configuration and supplies synthetic JWT
settings. Tests do not use a real database file or production connection string.

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
The generated AddPatientUserTenantIntegrity.sql is a review artifact, not applied
to a real database. Users and Patients are rebuilt. The preflight CHECK rejects
bad legacy links. A helper unique index supports SQLite's copy phase and is
removed when the old Users table is dropped.

Before deploying, back up and rehearse against an authorized isolated copy.
Normal non-Testing application startup still applies pending migrations, so do
not start the changed app against a real database before that review.

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
