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

## 2026-11-28: Invoice mathematics review (no invoice issuance)

An isolated internal snapshot calculates exact totals from explicitly supplied
pre-priced synthetic charges, discounts and taxes in integer units. Currency
format and scale are explicit, checked totals cannot silently overflow, and
existing estimate logic is unchanged. The standalone Release build and all 19
focused invoice tests passed before publication. No API, schema, tariff
assumption, merchant activation, receipt or payment confirmation was added.

## 2026-11-28: Settlement evidence comparison only

A pure internal comparison checks that separately verified capture evidence
matches an expected invoice, provider scope, seller and transaction, as well
as integer amount, currency and scale. Missing, mismatched, reused or
unknown evidence never qualifies. A matching result is only a candidate,
not settlement or authorization to issue a receipt. The Release build and
25 focused amount/settlement tests passed. No external gateway, ledger write,
payment state, receipt issue, schema or production change.

## Synthetic settlement claim concurrency boundary

Matched evidence alone is not enough to reserve a transaction. The internal
guard requires a trusted atomic insert-if-absent operation keyed by provider,
seller and transaction, independent of invoice ID. The synthetic concurrency
test deliberately lets two invoice contenders see the same stale negative
lookup and verifies only one claim is reserved. Same-invoice replay cannot
reserve a second time. Release build and all 31 focused tests passed.
Production storage, verified provider calls, ledger effects, invoice status,
receipts and deployment remain untouched.

## Settlement reservation and ledger atomicity on actual SQLite

The new synthetic fixture uses separate connections to a unique named
in-memory SQLite database and a single non-deferred transaction for claim
and ledger marker. A real ledger constraint abort leaves neither row;
two simultaneously open writers plus stale replay leave exactly one
matching pair. Release build clean, 33 focused tests passed; both new
cases passed five repeated runs. It is evidence for the transaction
boundary, not a deployed database implementation or paid invoice.

## Approved pricing scope for immutable draft invoices

The new conditional draft type binds clinic and encounter to a trusted
pricing result with explicit approval and tariff revision references. It
rejects missing/mismatched scope or approval and preserves exact amounts
when pricing changes later. Existing estimate/catalog state is not approval.
Release clean and 43 focused tests passed. No persistence, trusted pricing
implementation, invoice number or actual document is issued here.

## Verified settlement prerequisite for future receipt review

The internal check rejects missing, unknown, candidate or reserved evidence.
It requires a positive amount and trusted verified-committed ledger evidence
bound to the same opaque invoice identity, clinic, appointment, approval,
tariff revision, currency, scale and ordered monetary lines. Matching
amounts on a different invoice cannot pass. This is a precondition only:
nothing issues a receipt, produces a paid state, or supplies provenance,
runtime storage, authorization or tax policy.
Release build completed with no warnings or errors and 50 focused tests
passed, including seven new receipt prerequisite cases. Hosted regression
is tracked separately.

## Patient-facing draft invoice breakdown contract

The internal projection derives every ordered charge/discount/tax line and
the review total from the same immutable approved-price draft. It returns
an explicit non-payable draft status, uses a fixed non-clinical label key,
excludes patient/encounter/approval references and serializes integer units
as exact decimal strings with currency and scale. Oversized breakdowns fail
as a whole rather than showing an incomplete total. No endpoint, client
routing, tariff source, issued invoice, payment action or provider was added.
Release build passed with zero warnings/errors; all 54 focused invoice and
settlement tests passed, including the four projection regressions.

## Flutter My Bookings nonpayable draft acceptance

Isolated `dot/flutter-invoice-review` starts from exact draft #44
`1a58b8d`. Only a non-release review composition with an explicit
injected reader can select a booking's draft in My Bookings; otherwise the
existing estimate action is unchanged. The exact internal projection is
decoded as bounded signed-64-bit unit strings via BigInt, with contiguous
ordered rows, a fixed generic label and independently checked line/aggregate
arithmetic. Switching booking or ending the session discards prior results;
a mismatched or failed response displays no amounts. No payable invoice,
checkout, receipt, email, pricing fallback, endpoint, storage or backend
changes are included.

Flutter analysis passed with no issues. The final offline Flutter suite
passed 159/159, including eight model and real-My-Bookings widget mock cases.
The review has no live backend acceptance or deployed client evidence.

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

## Flutter signed clinical notes and append-only addenda

The clinician adapter submits original immutable notes and UUID-scoped
addenda and handles real acknowledgements without reading clinical data from
a write response. Ambiguous writes require an explicit audited read; no
automatic POST/retry or false success on conflict. Its focused 47/47 and
full 182/182 local Flutter checks passed; the HTTPS acceptance is extended
to sign, check immutable conflict, add, replay and read. Real data,
production and default UI remain outside this draft.

## Bounded clinician encounter selection

The synthetic-only clinician list returns the assigned, started, confirmed
appointments eligible for the existing procedure workflow. It releases only
appointment ID, UTC times and the booked service ID/label. No patient
identifier, clinical text, money, total or unaudited has-more hint is returned.
Audit rows commit before the page is released. Tests passed 3/3 focused and
608 local offline, with one independent opt-in Flutter HTTPS case skipped.
No schema, persistent database, default UI or production changes.

## Flutter authorized encounter selection contract

The bounded Flutter client consumes only five server-approved fields and
strict UTC bounds, rejects unexpected identifiers and totals, and does not
cache results. Independent analysis, focused 53/53 and full 188/188 passed.
The real synthetic HTTPS exercise now selects the server-supplied encounter
and service, checks the other clinic gets no rows, then signs and verifies
the audits. Treat hosted integration results separately from mocks.

## Review-only clinician path: sign in, select and confirm

The opt-in Flutter entry verifies a clinic Doctor, loads the audited
assignment list and passes the server-supplied appointment and booked
service to the confirmation panel. It clears state on logout or clinic
change and cannot change encounter during an unresolved write. No default
route, release activation, persisted clinical state or external access.
The isolated widget harness passed 196 Flutter tests and eight existing
panel tests independently. The combined hosted check runs at its own SHA.

## Review-only clinician note panel

Adds a standalone note/addenda panel for an already selected encounter.
Signing requires explicit confirmation, original text cannot be replaced,
and uncertain acknowledgement is reconciled through audited reads without
automatic writes. An addendum retry is explicit and reuses the same UUID
and exact intended note. Session or selection changes clear protected
state. Nine new widget tests brought independent full Flutter to 191/191
with a clean analyzer. Exact combined CI is tracked separately.

## Combined clinician review: note, addenda and performed procedure

The isolated review stack combines the exact clinician-entry and standalone
note-panel branches. Both tabs use the same authenticated clinic Doctor
adapter and the same server-selected encounter. Tab changes retain pending
in-memory intents without triggering a POST. While either tab has an active
or unresolved submission, the entry disables changing encounters and
normal back navigation. Explicit logout closes the transport and drops both
panels and all protected data; changing encounter or review configuration
clears the old view. No default app route or release activation was added.

The locally combined mobile sources passed the Flutter analyzer and 215
offline Flutter tests. The entry tests exercise shared selection, switching
with an unresolved original/procedure, different-content reconciliation,
and clearing protected state on reselection/logout. No combined hosted
HTTPS/CI, persistent database, production behavior, or broader clinical
workflow is verified by these widget and client checks.

## Real review-shell HTTPS acceptance

The native proof drives the opt-in Flutter shell through real Doctor login,
server-selected booked service, explicit procedure confirmation, audited
refresh and logout; a foreign-clinic Doctor sees no selectable rows. It
also retains the separate API acceptance for note/addenda and assignment
selection. The native harness passed 2/2 on the exact #34 parent before
combining the two clinical panels. This combined draft runs the harness
again; local widget regressions for the composed panels passed 215/215.

## Native clinician review acceptance: immutable note and signed addendum

The same opt-in Flutter review widget now has synthetic acceptance over
loopback HTTPS for procedure confirmation, deliberate original-note signing,
audited original read, append-only addendum, audited addendum discovery and
keyed read. Switching in either direction does not send a request and keeps
the result tied to the selected encounter. Logout and changing clinics hide
both notes. The independent adapter acceptance remains in the same suite.

The in-memory SQLite fixture verified exactly one original, one addendum and
one procedure for the approved clinic/encounter/doctor. The exact nine audit
events were two records, one amendment and six history reads, all scoped to
that encounter and doctor. Both loopback acceptances passed locally. Release
build was clean. This is synthetic acceptance only, not a rollout or a
verification of any persistent database.


## Director service assignment preserves unchanged inactive links

A catalog deactivation no longer prevents a Director from updating another
service on the same doctor's full replacement assignment form. An inactive
catalog link can be carried forward only if it is already stored and exactly
unchanged; new or edited inactive rows are still rejected, and omission
retains the existing deactivation policy. The synthetic HTTP and Flutter
regressions passed (44 focused backend, 18 focused Flutter). The rule does
not make an inactive service bookable or alter clinic visibility.

## Real-loopback Flutter appointment move acceptance

Isolated `dot/flutter-real-api-e2e` starts from exact draft PR #16
`83dc282`. The optional `FlutterLoopback` native widget case drove the real
patient login, history, date/slot picker, stale-slot 409 and explicit
identical-key retry over an ephemeral IPv4 Kestrel listener using only
synthetic in-memory SQLite in Testing. A real committed 200 was deliberately
discarded by the test transport before it reached the app; replay returned
200 without a second replacement. A history read proved the original was
unchanged on conflict, then cancelled only on success. The backend verified
one linked move ledger and no duplicate or cancellation notifications.

Release build passed with 0 warnings/errors; offline backend regression
passed 603/603, and the separately opted-in native loopback case passed 1/1.
No live PostgreSQL, external/DNS target, real notifications, device build,
browser CORS or deployed same-version environment was validated.

## 2026-11-28: Director tariff input validation

The Director create and edit APIs now require an explicit minimum price.
An omitted field no longer silently saves zero; an explicitly entered zero
remains valid. The existing Flutter client already sends the field. On the
isolated branch, 604 offline backend tests passed, including synthetic
Director create/update regressions and preservation of the existing tariff.
No schema, tariff policy, payment, live PostgreSQL or production changes.

## Patient estimate cache boundary

Appointment-linked price estimates and their email response now opt out of
HTTP caching. A synthetic HTTP test checks a no-store owner response,
404/no-store for other patients in both clinics, and no final amount,
tracked payment or issued receipt in the estimate DTO. The focused test and
Release build passed. No message, payment, receipt, schema or live connection
was made by this change.

## Flutter estimate contract and selection boundary

An estimate must match the selected appointment and booked service, remain
nonpayable and untracked, and contain no issued receipt or inconsistent price
range. Flutter refuses model mismatches and requests no-store without
following redirects. Switching inputs reloads the estimate and discards
late results from the old selection. A distinct real invoice contract is
still needed. This change sends no email, payment or receipt. Hosted
verification is tracked separately from local tooling.

## Five-tip release-review candidate (isolated, synthetic only)

The common foundation is #18. This candidate retains the exact source tips:

| PR | Head | Scope |
| --- | --- | --- |
| #45 | `c39211c4a10cda8d30d1a000c2712d29d4223015` | Finance prerequisites and release-disabled nonpayable review (first parent) |
| #43 | `9ced6c02bd263c33153cbbdb3b958685918211b8` | Clinical review HTTPS and Director assignment preservation |
| #23 | `c0234c88c65bb8932f49144ea2eb4693369803f9` | Patient rescheduling widget-to-API acceptance |
| #25 | `e533192d113422889ec943e4847a32dd9857b00f` | Explicit minimum tariff |
| #38 | `f399bdba8987f5cda4db9513b3490d8b5d8d0fdd` | Private, uncached patient estimate |

The documentation histories are carried forward and both loopback actions run
as separate steps with independent per-command opt-ins, test categories and
fixture teardown. Prior green PRs are evidence for those original revisions,
not for this combined candidate. Review this candidate's own check runs before
claiming combined acceptance. No live PostgreSQL, real providers, production,
deployments or default financial/clinician rollout are in scope.

## Follow-up to exact five-tip review

On #46 (`487de485560eb82209837b91c67cf9af59403619`), hosted run
46142163578 passed both jobs, including both real synthetic loopback steps.
Local Release was clean, default backend 665 passed with the three isolated
native tests opt-in, and Flutter 227 passed. The clinician native pair
passed locally; one local appointment-native run finished the lifecycle
but disposed its widget before its own history read timeout cleared.
The standalone test-only repair passed 1/1 locally by waiting for the real
history refresh. #46 is unchanged; this follow-up adds that test repair,
the narrow/200%-text nonpayable invoice review accessibility regression, and
an offline source inventory for 21 SQLite/11 PostgreSQL migrations.
These are not additional independent test totals for this combined tree.

## Invoice draft review HTTP test fixture

The test assembly can explicitly register a read-only invoice draft review
endpoint against a disposable Testing + in-memory SQLite host. Application
code does not compile or register the fixture controller. It uses the
existing authenticated, active patient booking ownership rules before
projecting the synthetic server-approved snapshot. The owner receives a
non-payable exact breakdown with no-store; unknown booking, another owner,
different clinic and absent/mismatched/incomplete approval share the same
generic unavailable response. No storage, invoice issuance, payment or
provider behavior was added.
Release build passed with no warnings or errors; six independent HTTP
fixture cases passed.

The combined Flutter client + fixture ran three native Flutter/Kestrel
loopback TLS cases: two unchanged clinician cases and one dedicated invoice
case. The invoice case checks owned review totals and no-store plus denial
for both another patient in the same clinic and a patient in another clinic.
Flutter analyze and all 232 normal Flutter tests passed locally. These are
synthetic checks, not evidence of an issued invoice or production backend.

## Academic-only loopback runtime

The standalone classroom command starts the real clinic API on IPv4 loopback
using a unique disposable SQLite in-memory database. A separate browser
entry enables only its fixed loopback origin. A smoke run against the
portable API build passed booking creation, replacement, cancellation,
history, profile, catalog and doctor lookup, verification with code captured
locally rather than sent, Director access and assigned Doctor API reads.
Existing production release setup is unchanged; no live data or services.
