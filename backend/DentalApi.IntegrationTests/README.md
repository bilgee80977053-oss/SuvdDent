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