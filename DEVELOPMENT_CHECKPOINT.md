# Development checkpoint

**Captured:** 2026-11-27
**Branch:** `feature/suvd-dent-backend-completion`
**Starting HEAD:** `13d314045202040cc3abe404baa250da82dc5387`
**Upstream before handover:** `origin/feature/suvd-dent-backend-completion`
**Implementation commit:** `cd0c066` (`feat(appointments): enforce database booking integrity`)

## Follow-up: appointment catalog verification

The previous handover fixed lookup of EF's truncated foreign-key name. A
separate mismatch remained: the catalog can render the exclusion predicate
with an explicit text cast on `"Status"` and an outer parenthesis pair. The
previous verifier rejected that documented output even when the constraint was
validated. The verifier now accepts the equivalent catalog forms and optional
spacing before the foreign key's referenced columns. It still requires the
same tables, columns, delete rule, exclusion operators, half-open range and
confirmed-only predicate.

The new offline regression failed on the handover revision and passed with
PowerShell 7.5.4 on the isolated Linux checkout after the fix. No execution
policy was changed. The new cases also reject another status, additional
conditions and unbalanced parentheses. No PostgreSQL server, migration,
credentials or patient data were used. Live verification and the write-capable
suite remain separately gated by operator approval.

## Verified implementation and tests

The backend includes appointment booking and provider-specific integrity
migrations. The latest source histories contain 16 SQLite migrations and 6
PostgreSQL migrations. Provider histories are independent; snapshots report no
pending model changes.

Executed on this checkout:

| Check | Result |
| --- | --- |
| .NET Release build (`--no-restore`) | Passed; 0 warnings, 0 errors |
| Offline backend regression filter | Passed; 520 passed, 0 failed, 0 skipped |
| SQLite model consistency | Passed; no pending model changes |
| PostgreSQL model consistency | Passed; no pending model changes |
| PowerShell parser, all 4 scripts/modules | Passed |
| PostgreSQL automation helper test script | Not executed: effective PowerShell execution policy is `Restricted`; policy was not changed or bypassed |
| Live PostgreSQL tests/migration validation | Not run; no live database was accessed |
| Flutter tests/analyze | Not run in this handover validation |

`NEXORA_PG_TEST_CONNECTION` and `PGPASSWORD` were confirmed absent before the
offline validation. The offline test filter excludes both PostgreSQL integration
categories; their behavior is not established by the 520 passing tests.

The constraint-name verifier allows a single trailing tilde on an otherwise
ASCII identifier, matching the generated truncated PostgreSQL FK name. Its
offline test cases cover acceptance of that name and rejection of multiple
terminal tildes and injection punctuation; the PowerShell test file itself
could not be executed under the current policy.

## Migration history

SQLite (`AppDbContext`), in order:

1. `20261021062135_initialCreate`
2. `20261021183338_AddEmailAndVerification`
3. `20261022082458_AddVerificationPurpose`
4. `20261025121740_AddPatientsTable`
5. `20261026074904_AddUserTokenVersion`
6. `20261026100808_AddPatientUserTenantIntegrity`
7. `20261026104238_AddDoctorProfiles`
8. `20261026120838_AddSchedulingFoundations`
9. `20261026124849_AddClinicLocationVerification`
10. `20261026131551_AddPlatformAccounts`
11. `20261027180600_AddAppointmentFoundation`
12. `20261102165207_AddNotificationOutbox`
13. `20261102180259_AddPhoneVerificationChallenges`
14. `20261125115238_AddClinicTenantRootForeignKeys`
15. `20261126070328_EnforceAppointmentIntegrity`
16. `20261126073027_ProtectAppointmentOverlap`

PostgreSQL (`PostgreSqlAppDbContext`), independent ordered history:

1. `20261027132306_PostgreSqlBaseline`
2. `20261027180725_AddPostgreSqlAppointmentFoundation`
3. `20261102165221_AddPostgreSqlNotificationOutbox`
4. `20261102180315_AddPostgreSqlPhoneVerificationChallenges`
5. `20261125115303_AddPostgreSqlClinicTenantRootForeignKeys`
6. `20261126070352_EnforceAppointmentIntegrity`

The migration list is source history, not evidence that any persistent database
has applied these migrations. No persistent database migration was run.

## Commands executed

```powershell
dotnet build .\backend\NexoraDental.sln --configuration Release --no-restore
dotnet test .\backend\NexoraDental.sln --configuration Release --no-build --filter "Category!=PostgreSqlIntegration&Category!=PostgreSqlConcurrency"
dotnet ef migrations has-pending-model-changes --context AppDbContext --project .\backend\DentalApi --startup-project .\backend\DentalApi
dotnet ef migrations has-pending-model-changes --context PostgreSqlAppDbContext --project .\backend\DentalApi --startup-project .\backend\DentalApi
.\backend\scripts\tests\PostgreSqlAutomation.Tests.ps1
```

The first four commands succeeded. The final command was attempted and blocked
by `Restricted` execution policy.

## Working-tree and privacy notes

The original uncommitted backend work was preserved and committed as `cd0c066`.
Untracked `.vscode/` settings and standalone lab/coursework SQL files were left
untouched and are excluded from the handover commits. The changed backend
content was reviewed for credentials, real connection strings, patient data and
unrelated coursework; only synthetic test data, fixed local test targets and
secret-handling code were found. Existing unrelated mobile coursework/student
metadata was not modified.

## Follow-up: advisory rescheduling slots (2026-11-27)

Branch: `dot/reschedule-availability`, based on the published handover
`5ad45df`. This increment adds the authenticated HTTP slot lookup and
Flutter client adapter for an owned future confirmed appointment. The
original remains unchanged; the lookup neither reserves a slot nor queues
a notification. Regression tests cover scope across patients/clinics,
ignored client identity overrides, future/state validation, and how only
the original appointment is excluded from conflict detection.

| Check | Result |
| --- | --- |
| .NET 10.0.402 Release build | Passed; 0 warnings, 0 errors |
| Offline backend regression filter | 522 passed, 0 failed, 0 skipped |
| New rescheduling HTTP tests | 2 passed using the in-memory SQLite fixture |
| Flutter 3.47.7 | Analyze clean; 124 tests passed |
| Live PostgreSQL or LIVE Flutter-to-backend | Not run; isolated target/preflight unapproved |

The remaining booking milestone is an atomic, idempotent commit of the
change and an enabled Flutter workflow. No provider schema change, live
database write, payment, or deployment was performed. The original Windows
handover evidence above is retained for provenance.

## Remaining blockers

- Execute the PowerShell helper tests in an environment that permits the
  repository script without changing/bypassing policy.
- Obtain operator approval for any live PostgreSQL read/write validation;
  constraint existence and migration behavior on an actual server remain
  unverified.
- Do not represent an estimate as a payment or receipt. Financial collection,
  production review and deployment remain incomplete.
## Verified milestone: atomic appointment move and Flutter LIVE (2026-11-27)

The isolated `dot/atomic-reschedule` review branch builds on `5ad45df`
and includes the work in PRs #1–#4. All six original PR pairs combined
without conflicts; integrating the new implementation with PR #2 required
preserving its cancellation documentation comment. Neither the primary
branch nor `main` was changed.

- An owned future booking can be moved to an available time, with the
  same clinic, patient, doctor and service. A successful transaction
  links the retained cancelled original to one new confirmed booking.
  An uncommitted move leaves the original appointment and queued
  notifications unchanged. Only eligible new notices are queued;
  the worker suppresses old pending confirmation and reminder entries.
- A duplicate exact request returns the recorded replacement, including
  its current status. Flutter keeps the same UUID for an uncertain
  response and reloads availability after a definitive 409. It does
  not mark the original cancelled based on an unconfirmed response.
- New model history is 17 SQLite and 7 PostgreSQL migrations. SQLite
  upgrade and rollback on disposable in-memory data preserved prior
  appointments and overlap triggers. Both migrations add only the
  clinic-scoped ledger; same-patient matching is verified in the service.

| Check | Verified result |
| --- | --- |
| .NET 10.0.402 Release | Build succeeded; 0 warnings, 0 errors |
| Backend offline | 535 passed, 0 failed or skipped; opt-in PostgreSQL excluded |
| Backend scope | Access/tenant, duplicate/replay, competing booking/move/cancel, commit rollback and pending notification suppression |
| EF model and SQL generation | Both provider snapshots current; additive changes checked offline |
| PowerShell | Four scripts/modules parsed; offline helper tests passed |
| Flutter 3.47.7 | Analyze clean; 135 tests passed, including 11 focused cases; mock HTTP, no live server |
| Flutter web release | JavaScript release built with a non-routable placeholder API; existing icon-font warning. Optional Wasm dry run not verified. |
| GitHub Actions | Workflow from PR #3 included; check the candidate PR for hosted results. |
| PostgreSQL/real Flutter-to-API | Not run. No server contacted and no persistent database migrated. |

The separately approved seventh migration is
`20261127122656_AddPostgreSqlAppointmentReschedules`. The existing 13 opt-in
PostgreSQL tests do not verify rescheduling. Follow the guarded
[PostgreSQL and Flutter LIVE checklist](backend/DentalApi/ReschedulingAcceptance.md)
after target verification and approval. Keep clinical history gated on durable
audit; billing, invoices, receipts and remaining clinic administration are not
accepted. Do not estimate whole-project completion without a bounded workload.
