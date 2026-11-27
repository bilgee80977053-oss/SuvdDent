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
migrations. This original handover captured 16 SQLite migrations and 6
PostgreSQL migrations; newer isolated review branches are recorded below. Provider histories are independent; snapshots report no
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

The seventh migration, which requires separate approval, is
`20261127122656_AddPostgreSqlAppointmentReschedules`. The existing 13 opt-in
PostgreSQL tests do not verify rescheduling. Follow the guarded
[PostgreSQL and Flutter LIVE checklist](backend/DentalApi/ReschedulingAcceptance.md)
after target verification and approval. Keep clinical history gated on durable
audit; billing, invoices, receipts and remaining clinic administration are not
accepted. Do not estimate whole-project completion without a bounded workload.


## Clinical access and audit overlay on atomic rescheduling (2026-11-27)

The review-only `dot/clinical-on-reschedule` branch is based on
`ed373eb` (atomic rescheduling). It adds an internal appointment-scoped
check for the active assigned doctor and prevents clinical fields from being
read or updated through demographic profile routes. It does not expose a
clinical endpoint or add treatment records. The audit service stages only
same-clinic actor/encounter identifiers, action, UTC time and a correlation
UUID. A future audited clinical edit must commit its event and record together;
a history read must not release records if its audit cannot be persisted.

Source inventory is now **18 SQLite and 8 PostgreSQL migrations**. The
rescheduling migration IDs and history are unchanged. The clinical audit
migrations (`20261127123139_AddClinicalAuditTrail` and
`20261127123142_AddPostgreSqlClinicalAuditTrail`) follow them and add only
a new table, indexes, and provider-specific append-only triggers. The
SQLite upgrade test verifies an existing reschedule link and all four appointment
overlap triggers survive audit migration. PostgreSQL SQL is reviewed offline.

The live PostgreSQL integration and rehearsal wrappers, direct test
preflight and fixture executables must remain fail-closed at the last
reviewed seven-migration source configuration. No audit migration approval
or workflow token was added. This eight-migration overlay is offline-only;
do not run it against an active PostgreSQL database. No PostgreSQL server
was contacted or persistent database migrated.

| Local overlay check | Result |
| --- | --- |
| .NET 10.0.402 Release | Build succeeded; 0 warnings, 0 errors |
| Focused clinical, tenant and provider migration tests | 64 passed, 0 failed, 0 skipped |
| Full offline backend regression filter | 539 passed, 0 failed, 0 skipped; live PostgreSQL categories excluded |
| SQLite audit upgrade | Preserved existing reschedule linkage and all four appointment overlap triggers; synthetic rollback and immutability passed |
| Provider model and SQL | Both EF snapshots current; audit SQL adds no changes to existing tables; PostgreSQL checked offline only |
| PowerShell | Four files parsed; offline automation helper tests passed |

## Assigned encounter treatment note draft (2026-11-27)

On isolated `dot/treatment-encounter`, based on the clinical audit overlay
(`f2a974f`), a doctor assigned to an active same-clinic patient and started
confirmed encounter can sign one immutable bounded plain-text note and read
it back only while still authorized. The note and its metadata-only write
audit commit together. A read audit must commit before returning the note.
Neither failures nor POST successes return the note. No cache is requested.

The route is off by default. Explicit opt-in works only in Development or
Testing with a memory-backed SQLite connection. No persistent SQLite file,
PostgreSQL, or production environment is accepted by this draft route. Do not
start this draft against a persistent database, since the pre-existing SQLite
startup path can apply migrations outside Testing.

Provider source histories now have 19 SQLite and 9 PostgreSQL migrations,
including `20261127124415_AddTreatmentRecords` and
`20261127124418_AddPostgreSqlTreatmentRecords`. The new scripts create only
the treatment table, its index and mutation/signer guards. The live
PostgreSQL seven-migration gates remain unchanged and closed. No server was
contacted and no persistent migration was applied.

| Local draft check | Result |
| --- | --- |
| .NET 10.0.402 Release | Build passed; 0 warning/error |
| Focused treatment, audit, authorization and provider tests | 30 passed, 0 failed, 0 skipped |
| Full offline backend filter | 553 passed, 0 failed, 0 skipped; live PostgreSQL categories excluded |
| Synthetic HTTP/storage | Default-off/target guard, assigned signer, cross-clinic denial, duplicate and immutable notes, read audit denial, commit rollback, and no-store checks passed |
| PowerShell helper | Passed offline; no database connection |

Remaining: immutable amendments, clinical procedure and tooth definitions,
scoped longitudinal history, retention/role review, Flutter UI and all
billing. Do not treat this synthetic draft as production acceptance. See
[the draft API contract](backend/DentalApi/EncounterTreatmentApi.md).

## Signed treatment addenda draft (2026-11-27)

On isolated `dot/treatment-addenda`, based on `7c2c359`, the original
assigned clinician can sign an addendum to an existing encounter note under
the same off-by-default Development/Testing in-memory SQLite gate. The
original and each addendum remain immutable. The client UUID is scoped to the
encounter; exact replay creates no duplicate event or record, while changed
payload with that UUID conflicts. A new addendum and its `TreatmentAmended`
audit commit together. Only a single addendum may be read by key, only by its
currently authorized signer and only after durable access audit. No list or
patient-wide history is exposed.

Source history is now 20 SQLite and 10 PostgreSQL migrations. Provider
migrations `20261127125153_AddTreatmentAddenda` and
`20261127125155_AddPostgreSqlTreatmentAddenda` add only the new table, index
and signer/immutability guards; earlier migrations are unchanged. Existing
seven-migration PostgreSQL live workflow gates remain closed. PostgreSQL model
and SQL checks are offline only. No persistent database migration, real data,
or GitHub publication was performed in this worktree.

| Local draft check | Result |
| --- | --- |
| .NET Release | 0 warnings, 0 errors |
| Focused treatment, audit, authorization and provider checks | 35 passed, 0 failed, 0 skipped |
| Full offline backend regression filter | 558 passed, 0 failed, 0 skipped; live PostgreSQL categories excluded |
| Synthetic API/storage | Unmodified original, keyed replay, cross-clinic denial, commit rollback, failed read audit denial, immutability and no-store all passed |
| PowerShell helper | Passed offline |

## One-encounter addenda discovery draft (2026-11-27)

Isolated `dot/encounter-addenda-discovery`, stacked on `c7acf32`, adds
metadata-only discovery pages for the same still-authorized original
signer under the existing off-by-default in-memory SQLite gate. Pages return
only addendum UUID and signed UTC, sorted by UTC and then UUID, at most 50
entries plus a next-page indicator; they do not select any note text. Every
released page, including a valid empty page, first commits its access audit.
A missing original, inadmissible actor, or audit failure does not release data.

No migrations were added. SQLite/PostgreSQL source history remains 20/10;
the seven-migration live PostgreSQL guard remains closed. Original and signed
notes stay immutable. No patient-wide history, Flutter UI, procedure/price
catalog changes, real database use or remote publication was included.

| Local draft check | Result |
| --- | --- |
| .NET Release | 0 warnings, 0 errors |
| Focused treatment, audit, authorization and provider checks | 37 passed, 0 failed, 0 skipped |
| Full offline backend regression filter | 560 passed, 0 failed, 0 skipped; live PostgreSQL categories excluded |
| Synthetic discovery | Paging bounds, UUID tie-break order, metadata-only projection, denied cross-tenant/unassigned access, no-store and fail-closed audit passed |
| PowerShell helper | Passed offline |

## Flutter clinician API adapter draft (2026-11-27)

The isolated `dot/clinician-mobile` branch stacks on PR #10. Its verified
code commit `0cf62d7` adds only a typed Flutter adapter and mock HTTP
tests. Clinic, user and Doctor role are verified before the adapter accepts
a session. It reads a single encounter note, a page of addenda references,
or one keyed addendum. The adapter keeps only the active token in memory;
it does not log, cache or persist clinical responses and discards pending
responses after logout. Errors do not surface server-provided content.

- Flutter analyze: no issues. Full offline Flutter test suite: 151/151,
  including 16 new clinician mock HTTP cases.
- No default UI wiring, treatment writes, broader discovery, dependencies,
  backend changes, migrations, or live traffic. The backend clinical
  routes remain off by default and gated to Development/Testing with
  in-memory SQLite. No same-version end-to-end run was performed.
- Next: test the gated clinic workflow end-to-end on authorized synthetic
  data, and keep audit/privacy checks before any actual clinician release.

## Routine tooth target groundwork

The isolated `dot/routine-tooth-scope` branch starts from exact clinician
adapter draft head `88afa04`. It adds an internal validator for the 52
routine FDI codes and explicit visit-versus-single-tooth scope. It adds no
HTTP route, data model, migration, provider operations, age inference, or
clinical procedure workflow. Existing audit, signed notes and pricing are
unchanged. See [validation scope and sources](backend/DentalApi/StructuredTreatmentTarget.md).

Release build passed with 0 warnings/errors. All 27 focused cases passed,
including the exhaustive 16,384 ASCII-pair test. The full offline backend
filter passed 587/587 (0 failures/skips; live PostgreSQL excluded). The offline
PowerShell helper passed, and no live database was contacted.
