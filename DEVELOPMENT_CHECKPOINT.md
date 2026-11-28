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

## Structured procedure validation draft

The isolated `dot/structured-procedure-draft` starts from exact PR #12 head
`c000f65`. Its internal validation binds a routine tooth/visit target to a
current authorized encounter and active same-clinic service ID/label. It
returns only immutable nonfinancial identity and label data. No audit row,
record, HTTP route, migration, tariff or payment support was added; no live
database was contacted. See
[the structured draft and remaining gates](backend/DentalApi/StructuredTreatmentTarget.md).

Release build passed with 0 warnings/errors; the focused service and target
tests passed 30/30 and the full offline backend suite 590/590, with 0 failures
or skips. The offline PowerShell helper passed. Tests exercised the exact
internal service entry point on synthetic in-memory SQLite, not an HTTP
procedure flow or PostgreSQL server.

## Internal performed-procedure item draft

Isolated `dot/performed-procedure` starts from exact PR #13 head `e4d4bad`.
The synthetic Testing-only service signs an immutable nonfinancial item for
the activated same-clinic catalog service already booked on the begun
confirmed visit. The active assignee and matching current qualification are
revalidated in the same transaction. Item and metadata-only audit are
atomic; replay is visit-scoped and a changed target cannot overwrite the
record. Catalog rename leaves the signed label unchanged. No read/list or
signing HTTP route, financial action, real database, or existing clinical
note/addenda behavior changed.

Source histories are 21 SQLite and 11 PostgreSQL migrations. The respective
additive new drafts are `20261127150423_AddPerformedProcedures` and
`20261127150425_AddPostgreSqlPerformedProcedures`. Historical migrations and
seven-migration live PostgreSQL safeguards were not loosened. No PostgreSQL
server was contacted or persistent migration applied.

Exact Release build passed with 0 warnings/errors. Focused tests passed
48/48 and full offline backend 596/596 (0 failures/skips; live categories
excluded); the offline PowerShell helper passed. Synthetic tests verified
booked/assigned/catalog boundaries, immutable label, replay, commit and
audit-insert rollback, no unrelated tracked writes, same-scope retry and
SQLite immutability. Both migration snapshots and generated additive SQL
were checked offline.

## Performed-procedure synthetic HTTP acceptance draft

Isolated `dot/performed-http-acceptance` starts from exact PR #14 head
`d6f6d21`. It adds an explicit off-by-default clinic JWT Doctor POST,
only in Testing with in-memory SQLite and its dedicated flag. It reaches
the already-tested booked-service/qualification/audit transaction. One
route-bound clinical result is returned after commit; no patient or
clinician ID, money, price or payment receipt is returned. Exact retry is
empty, changed payload with the same UUID is refused, and no reader was added.

No migrations, provider histories, or original note/addendum routes
changed. PostgreSQL was excluded and never contacted. The nonmemory HTTP
check presents file-connection metadata after synthetic in-memory
authentication and verifies no persistent connection attempt. Model-only
PostgreSQL refusal is not a live-server result.

Exact Release build passed with 0 warnings/errors. Focused acceptance passed
47/47 and the full offline backend suite 601/601 (0 failures/skips), with
PowerShell helper passed. Synthetic HTTP cases verified strict shape, 1 KiB
declared-body bound, clinic JWT role and tenant isolation, opted-in memory gate,
out-of-environment and file rejection, qualification, signed result, empty
replay, conflict and sanitized transaction failure. Real listener/chunked-body,
live PostgreSQL, production rollout and an audited read flow were not verified.

## Same-encounter performed procedure read acceptance

Isolated `dot/performed-procedure-read` starts from exact PR #15 head
`7d56233`. The read route uses the existing synthetic opt-in gate and clinic
JWT Doctor authorization. It intersects the currently active assignment and
original row author for one begun confirmed encounter. Results return only
bounded clinical fields, deterministic signed-UTC/UUID order, no totals and
no-store. Each page requires committed metadata-only access audit, including
empty pages; audit failure does not return results. No new database schema,
other clinical routes or broad audit/history roles were added. No live server,
persistent target, rollout or remote publication was used.

Release build passed with 0 warnings/errors; focused 49/49 and full offline
backend 603/603 (0 failures/skips, opt-in PostgreSQL excluded), with PowerShell
helper passed. Synthetic HTTP proved role/tenant/assignment and original
author separation, fixed-UTC tie ordering, signed label after catalogue
rename, read auditing of empty pages and failure closed on audit insert
or commit. Live PostgreSQL, production clinical roles and patient history
were not verified or enabled.

## 2026-11-28: Stacked CI and Flutter performed-procedure review

The owner resumed isolated development and draft PR publication. PostgreSQL
live access, production, and main merges remain unauthorized. PR #1–#4 are
ancestors of #6. #7 includes #5 and #6 and resolves the overlapping checkpoint
and handover edits. Review #7–#16 in base order. Validation-only draft #17 ran
the exact #16 tree `40e2e489`; GitHub Actions run 46120829550 passed 603
offline backend tests, 151 Flutter tests and the PowerShell helper. Draft #18
extends only the offline PR trigger to stacked development branches.

The previous local Flutter commit `7efc7e9` was not available after the
environment restart; historical test logs are not evidence for this new tree.
This draft reconstructs the clinician adapter and mock tests against #16.
The adapter supports only single-encounter signing, replay and bounded reads,
with HTTPS and no default UI wiring. See this draft's distinct CI for results.
Flutter-to-API acceptance, clinic-wide history policy and live PostgreSQL
validation remain outstanding. No production workflow or real patient data
is involved.

## Synthetic clinician HTTPS acceptance review

An opt-in test harness starts the real DentalApi on an ephemeral IPv4
loopback HTTPS listener. It runs the Flutter clinician adapter against the
actual JWT login, identity, signing and read endpoints with disposable
in-memory SQLite fixtures. It checks exact replay, changed replay rejection,
cross-clinic denial and one committed signature plus the matching audit
events. It uses no system trust changes or external listener. The regular
backend suite skips this explicit opt-in, and the Flutter CI job invokes it
separately. GitHub Actions run 46121334606 passed the dedicated opt-in
(1/1), the ordinary offline backend suite (603 passed, 1 explicit opt-in
skip), Flutter (169 passed; analyzer clean), and PowerShell. Both jobs used
the same functional tree as PR #20 head `3f991cd`.

## Test-only anchor-bound patient period history

Isolated `dot/patient-period-history` starts from exact PR #16 head `83dc282`.
A current assigned doctor may discover visit IDs and start UTC for the same
clinic/patient from an authorized anchor, only within a bounded period and
only for visits currently assigned to that doctor with their signed procedure.
No broader patient browsing or personal and clinical text is released.
The anchor and each distinct disclosed visit share an atomic metadata audit;
failure blocks the whole page. Double opt-in and Testing/in-memory SQLite
guards remain closed outside synthetic testing. No new migration, rollout,
persistent database, live PostgreSQL or remote write was used.

Exact Release build passed with 0 warnings/errors. Focused synthetic HTTP
and existing clinical regression tests passed 39/39, and the full offline
backend suite passed 605/605 (0 failures/skips, opt-in PostgreSQL excluded).
Tests verify same-clinic/author/patient isolation, bounded and empty pages,
both flags, no persistent open attempts, and rollback of all page audit data
when a non-anchor audit row fails. PowerShell, real listener and production
policy were not tested in this slice.

## Flutter patient visit history integration

The staged mobile client validates a bounded, read-only UTC-period history
response and discards stale session results. On the isolated app/API stack,
the clinician HTTPS acceptance now includes authorized same-patient visit
reads and denial for another clinic. The independently tested Flutter diff
passed 175/175 offline tests and static analysis. Hosted loopback verification
is tracked separately; clinical UI and broader history policy remain pending.
