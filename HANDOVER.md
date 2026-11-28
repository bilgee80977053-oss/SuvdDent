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

## Latest booking milestone

The isolated `dot/atomic-reschedule` review branch combines PRs #1–#4
and adds the patient reschedule endpoint and LIVE Flutter flow. The source
stays booked if the replacement cannot be confirmed. A successful move
retains the cancelled source, creates a linked replacement under the same
patient and clinic, and queues only eligible new notifications. A durable
request key protects retries; Flutter reloads slots after a conflict and does
not silently submit another move.

The provider-specific migration adds only `AppointmentReschedules` and its
constraints/index. No PostgreSQL server was contacted. The seventh
PostgreSQL migration requires its own approval and must not be run on any
unauthorized database. See `DEVELOPMENT_CHECKPOINT.md` for exact offline
evidence and remaining PostgreSQL/Flutter-to-backend validation.

## Clinical review overlay

The `dot/clinical-on-reschedule` branch stacks encounter access and
append-only audit metadata on the rescheduling candidate. It adds no clinical
API or treatment records. Its current combined snapshots have 18 SQLite and
8 PostgreSQL migrations; the original rescheduling history is unchanged.
Use this branch for offline review. The PostgreSQL live runners and fixtures
remain intentionally fenced at seven migrations, so this overlay is not for
a live rehearsal or application startup with the old PostgreSQL history.
Before clinical exposure, implement the audited treatment record and verify
role separation, amendments, scoped history and retention against the
[clinical acceptance criteria](backend/DentalApi/ClinicalHistoryAcceptance.md).

## Assigned treatment note draft

The `dot/treatment-encounter` branch adds one immutable treatment note per
started confirmed encounter. Only the assigned active doctor can sign or read
it, only with explicit opt-in in a Development/Testing host and in-memory
SQLite. Its scoped write and read audit are required before success. It adds
no history list, amendment, procedure/tariff snapshot, Flutter UI, or billing.
The combined source currently has 19 SQLite and 9 PostgreSQL migrations; both
clinical migrations remain outside the seven-migration live controls. See
[the draft API contract](backend/DentalApi/EncounterTreatmentApi.md).

## Signed addenda draft

The next isolated `dot/treatment-addenda` draft preserves the initial note
and lets its original, still-assigned clinician append a separate signed note
with a stable client UUID. A write and its `TreatmentAmended` audit commit
together; exact replay does not create another record. Retrieval is one keyed
addendum after a successful read audit. The in-memory-only gate is unchanged.
Source migration inventory is now 20 SQLite / 10 PostgreSQL, still outside the
seven-migration live workflow. No patient-wide history, billing or actual
database use was introduced.

## One-encounter addenda discovery draft

The isolated `dot/encounter-addenda-discovery` draft adds metadata-only pages
for a single encounter so its still-authorized original signer can locate
addenda without already knowing a UUID. It uses the existing synthetic
in-memory gate and commits an access audit before each page. No SQL model
migration, patient-wide browsing, Flutter integration or payment work was
included. See [the draft API contract](backend/DentalApi/EncounterTreatmentApi.md).

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
  idempotency, concurrency protection and provider conflict mapping. Patients
  can cancel their own future appointments through the API and Flutter LIVE
  history. Repeated cancellation preserves the response without another
  notification event.
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
- Same-version LIVE cancellation and rescheduling verification, clinic
  discovery/onboarding, audited clinical records and
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

At the original Windows handover on 2026-11-27, Release build succeeded with 0 warnings/errors; the offline
backend filter passed 520 tests with no failures or skips; both provider model
checks reported no pending changes; all four PowerShell files parsed. The
PowerShell helper tests were blocked by execution policy. Live PostgreSQL
validation was not performed. See `DEVELOPMENT_CHECKPOINT.md` for exact
commands, counts and limitations.

## Immediate next actions

1. Review the booking and clinical-access pull requests; do not merge into `main`.
2. Ask the database operator to validate the isolated PostgreSQL target and
   approve any read-only preflight separately from each write-capable operation.
3. Review SQL/preflight findings, apply only approved migrations to an isolated
   rehearsal database, then execute the approved live PostgreSQL test suite.
4. Verify Flutter booking, reschedule, retry and cancel end-to-end against the
   same reviewed backend revision on an authorized synthetic environment.
5. Continue from `DEVELOPMENT_ROADMAP.md`; keep financial and production work
   behind their own security, business and operator reviews.

## Flutter clinician adapter follow-up

`dot/clinician-mobile` adds a staged, read-only clinician client atop the
one-encounter API. It is not connected to the default Flutter app. Use
only mock HTTP for its unit tests, and keep credentials and returned
record contents out of logs and persistent storage. Backend development
gates and the authorized PostgreSQL rehearsal boundary are unchanged.
See `DEVELOPMENT_CHECKPOINT.md` for the 151 passing Flutter tests and the
remaining end-to-end acceptance work.

## Routine structured treatment target

The separate routine-tooth groundwork adds only internal domain validation:
visit scope has no tooth, and single-tooth scope requires a canonical
routine FDI identifier. It does not enable a procedure workflow or change
audited notes, prices, database schema or live controls. See
[validation scope and sources](backend/DentalApi/StructuredTreatmentTarget.md).

## Structured procedure validation draft

The separate structured-procedure draft combines a validated routine
tooth/visit target with server-resolved current encounter identity and an
active same-clinic catalog label. It is an internal, nonfinancial snapshot,
not authorization to perform or sign a procedure. No route, persistence,
schema, audit behavior, original note or price changed. See
[remaining authorization and signing gates](backend/DentalApi/StructuredTreatmentTarget.md).

## Internal performed-procedure write draft

`dot/performed-procedure` uses an assigned active doctor and the begun
confirmed visit's booked service only, with an active matching qualification
and active catalog entry. It signs an immutable nonfinancial item and audit
atomically in Testing in-memory SQLite only, with no route. Earlier notes
and addenda do not change. The draft source counts are 21 SQLite and 11
PostgreSQL; live PostgreSQL remains fenced at seven reviewed migrations.
See [the acceptance boundary](backend/DentalApi/StructuredTreatmentTarget.md).

## Performed-procedure synthetic HTTP acceptance

`dot/performed-http-acceptance` adds a single explicit opt-in POST for
Testing with in-memory SQLite only. Clinic JWT Doctor auth and the existing
assignment, booked service, qualification and audit-write transaction
remain in force. The signed response contains clinical fields only; replay
has no body and no reader, procedure price or billing is exposed.
[The clinical draft contract](backend/DentalApi/StructuredTreatmentTarget.md)
documents shape and rollout limits. No migration or real persistent target
was used.

## Same-encounter performed procedure reader

`dot/performed-procedure-read` adds bounded no-store pages of the
current assigned doctor's own signed records on just one encounter,
under the same opt-in Testing/in-memory gate as POST. It does not let
a reassigned clinician read another author's rows or access patient-wide
history. Each authorized page, including an empty one, requires a
metadata-only audit to commit before response. No schema or rollout changed.

## Resumed work: performed procedures in Flutter

Start review with #6, then #7 (which includes #5), and follow the declared
stack through #16. Validation-only #17 is not a merge candidate; its hosted
run 46120829550 verified the same source tree as #16. #18 enables offline
checks for stacked PRs without adding any deployment, write permission or
PostgreSQL access. The reconstructed Flutter adapter is independently
reviewable above #18. A synthetic HTTPS sign/read/replay check is a separate
milestone; do not present mocks as full client-to-server acceptance.

## Local clinician HTTP acceptance command

After locked Flutter package restore, set `SUVDDENT_FLUTTER_TOOL` to the local
Flutter executable and run the backend integration test project with
`--filter 'Category=ClinicalFlutterLoopback'`. Leave both PostgreSQL opt-in
variables absent. The harness only binds loopback and uses an ephemeral
certificate and in-memory fixtures. GitHub Actions run 46121334606 verified
the clinician HTTPS bridge on head `3f991cd`. It is not a substitute for device,
clinical UI, live PostgreSQL or production acceptance.

## Test-only anchor-bound patient period history

`dot/patient-period-history` limits one synthetic date-range page to the same
clinic/patient from a currently assigned anchor and the same doctor's own
signed visits. It discloses only visit ID and start UTC; any page is released
only after all anchor/returned-visit audit entries commit. Both explicit flags
and the Testing/in-memory gate are required. No broader production policy,
schema or real data is implied. See
[patient history acceptance](backend/DentalApi/PatientPeriodHistoryAcceptance.md).

## Flutter history follow-up

This integration combines the #20 clinician transport and #21 patient
history boundary. Its one-encounter anchor cannot be used to bypass the
same-clinic, same-patient, active-assignment and original-signer checks.
Do not persist returned history or enable this synthetic flow in default UI.

## Clinical note confirmation on the synthetic branch

Resolve an uncertain original note with the audited original GET. Keep the
same addendum UUID for an explicitly retried addendum; use its audited keyed
GET to reconcile uncertain results. The Flutter adapter verifies a suitable
Location for creation without following redirects. A separate cross-origin
browser deployment review must address Location exposure; no CORS expansion
or production route is implied by this work.

## Clinical selection prerequisite

The assigned list is Testing + in-memory SQLite only and explicitly off by
default. The authenticated static date bounds and page size are described
in the clinical history acceptance guide. The future Flutter selector should
use only server-returned encounter and booked service data and clear the
page on session changes; never ask a doctor to guess a patient or clinic ID.

## Assigned encounter mobile integration

The isolated acceptance stack combines #28 and #30 only on a review branch.
Its HTTPS harness uses the server-approved encounter and booked service
before signing. The default app route is unchanged.

## Clinician review entry

`ClinicianReviewEntry` is a standalone widget, disabled unless explicitly
enabled and always disabled in release. It requires a configured HTTPS
clinic, caller-owned transport factory and a bounded UTC period. It never
links from the default patient app. Preserve the panel's exact UUID until
an uncertain submission is reconciled, and dispose session data on logout.
