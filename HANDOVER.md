# Autonomous development handover

## Branch and scope

Target branch: `feature/suvd-dent-backend-completion`, tracking
`origin/feature/suvd-dent-backend-completion`. This handover preserves the
completed working-tree implementation, adds agent-facing documentation, and
does not merge to `main` or deploy.
The implementation commit is `cd0c066`; the handover documentation is committed
separately.

The current branch contains an ASP.NET Core 10 API, EF Core SQLite and PostgreSQL
providers, an xUnit integration suite, guarded PostgreSQL tooling, and a Flutter
DEMO/LIVE client. The exact test outcomes and unapplied migration history are in
`DEVELOPMENT_CHECKPOINT.md`; ordered next work is in `DEVELOPMENT_ROADMAP.md`.

## Clinical history handoff

The isolated clinical access milestone adds an internal appointment scope,
not a clinical history endpoint. Preserve the patient-profile privacy
boundary. Add durable audit metadata and verify atomic audit+record writes
before exposing clinical records. Review tenant-safe record storage, role
separation, retention and the history scope using the
[clinical acceptance criteria](backend/DentalApi/ClinicalHistoryAcceptance.md).

## Architecture

- `backend/DentalApi/Program.cs` composes controllers, DI, auth, rate limiting,
  health checks and provider selection.
- `Data/`, `Migrations/` and `Migrations/PostgreSql/` hold EF models and
  independent provider histories. Select `AppDbContext` for SQLite and
  `PostgreSqlAppDbContext` for PostgreSQL.
- Controllers expose clinic auth/patients/doctors/scheduling/locations,
  appointment booking, platform auth, staff/admin services, notifications and
  payment estimates. Services contain the business rules and ownership checks.
- Clinic JWTs and Platform JWTs are separate. Clinic claims are revalidated
  against active database users, clinic ownership, role and token version.
- `backend/DentalApi.IntegrationTests/` contains isolated in-memory SQLite,
  provider-model and offline safety tests. Live PostgreSQL tests are opt-in.
- `backend/scripts/` contains fixed-target PostgreSQL preflight, migration
  preparation/rehearsal and regression helpers.
- `mobile/lib/` contains the Flutter flow. DEMO uses synthetic data; LIVE requires
  explicit development API and clinic-ID configuration.

## Implemented workflows

- Clinic accounts, role-based auth, password hashing, patient self-service,
  profiles, doctor catalog, schedules, availability, clinic location/timezone
  verification and Platform account foundation.
- Notification preferences/outbox and phone verification challenge workflows.
- Patient booking discovery/create/list/detail with server-derived tenant
  identity, explicit time offsets, verified eligibility, snapshots, request-ID
  idempotency, concurrency protection and provider conflict mapping.
- SQLite migration preflights and overlap triggers; PostgreSQL composite
  doctor-service FK and GiST exclusion constraints. The PostgreSQL verifier
  safely accepts the EF-generated FK identifier's single trailing `~`.
- Mobile eight-stage demo/live booking flow and preliminary payment estimate.
  The estimate is explicitly non-final and does not charge or issue a receipt.

## Incomplete workflows and known limits

- No live PostgreSQL test, migration rehearsal or persistent database migration
  has been run for this handover. Model consistency and generated SQL checks are
  offline only.
- Current PowerShell helper tests could not execute because effective script
  policy is `Restricted`. The files parsed successfully; no policy bypass or
  change was attempted.
- Flutter analysis/tests and authorized live end-to-end checks were not run in
  this task.
- Payment settlement, gateway callbacks, refunds, invoices, eBarimt/legal
  receipts, approved tariff versioning/audit and payment reconciliation are not
  implemented. Do not call an estimate a payment or receipt.
- Clinic discovery/onboarding, booking cancellation/rescheduling lifecycle and
  production-grade delivery/audit/operations require follow-up.
- Migration source history does not prove a deployed database is at that
  version. The exact provider histories are recorded in
  `DEVELOPMENT_CHECKPOINT.md`.

## Security boundaries and operator requirements

- Never put credentials, real connection strings, patient data or backups in
  source, command history, reports or artifacts.
- Tenant identity must come from validated claims and database ownership, never
  arbitrary request-supplied clinic IDs. Keep clinic and Platform token schemes
  separate.
- PostgreSQL startup is guarded, read-only when explicitly approved, and never
  migrates/seeds. SQLite startup may automatically migrate outside `Testing`;
  do not start against an unknown database.
- Live integration and migration rehearsal scripts are fixed to isolated local
  targets, but still include write-capable paths. Require explicit operator
  approval before running them.
- Do not apply SQLite SQL to PostgreSQL. Do not perform destructive database
  actions, modify real patient data, deploy, or merge this branch to `main`.

## Validation summary

On 2026-11-27, Release build succeeded with 0 warnings/errors; the offline
backend filter passed 520 tests with no failures or skips; both provider model
checks reported no pending changes; all four PowerShell files parsed. The
PowerShell helper tests were blocked by execution policy. Live PostgreSQL
validation was not performed. See `DEVELOPMENT_CHECKPOINT.md` for exact
commands, counts and limitations.

## Immediate next actions

1. Run the offline PowerShell helper test in a policy-approved environment.
2. Ask the database operator to validate the isolated PostgreSQL target and
   approve any read-only preflight separately from each write-capable operation.
3. Review SQL/preflight findings, apply only approved migrations to an isolated
   rehearsal database, then execute the approved live PostgreSQL test suite.
4. Run Flutter analysis/tests and a development-only end-to-end workflow when
   an authorized environment is available.
5. Continue from `DEVELOPMENT_ROADMAP.md`; keep financial and production work
   behind their own security, business and operator reviews.
