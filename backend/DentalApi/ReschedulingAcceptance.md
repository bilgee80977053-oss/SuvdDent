# PostgreSQL + Flutter LIVE rescheduling acceptance

**Status: checklist only; not executed.** This gate verifies the same candidate
revision in Flutter LIVE, ASP.NET Core and an isolated PostgreSQL 18 database.
Offline SQLite, Flutter mocks, or the existing 13 PostgreSQL provider tests do
not satisfy this gate. Test only with disposable synthetic records.

## 1. Confirm scope and approval before operating

- [ ] Record the exact candidate commit to build for both API and Flutter, the
  private operator/host, intended target database and role, and the allowed
  synthetic clinics. Confirm each target by name. Never use production, a shared
  or unidentified database, or a database containing real patient data.
- [ ] For the repository's guarded provider suite, use only its fixed local
  `127.0.0.1:5432` PostgreSQL 18 target `nexora_dental_test`. Preserve its target,
  role, schema and migration checks. In the existing runner, only the operator's
  explicit `CONNECT_TO_TEST_PREFLIGHT` authorizes read-only preflight. Provide
  the database credential through the script's private secure prompt, never as
  an argument or in chat, source, Flutter defines, logs or attachments.
- [ ] The seventh PostgreSQL migration is
  `20261127122656_AddPostgreSqlAppointmentReschedules`, after
  `20261126070352_EnforceAppointmentIntegrity`. Review the generated SQL and
  exact ordered history/product version. The new migration only creates the
  ledger and its unique replacement index; it should not rebuild the appointment
  table or remove its exclusion constraints. Do not apply it without a distinct
  `APPLY_APPOINTMENT_RESCHEDULE_MIGRATION` approval on the runner's verified
  target. If earlier migrations are missing, stop or use the existing successive
  one-migration approvals documented in [the runner guide](../scripts/README.md).
  An approval for one migration does not authorize any other.
- [ ] On the complete history, separately approve `RUN_13_POSTGRESQL_TESTS`
  for the existing suite only. Record its structured result (13 passing,
  zero failing/skipped or an explicit failure). That token does **not** authorize
  the Flutter LIVE scenarios, a fixture, other database writes or a reset.
- [ ] Obtain independent explicit approval for the same-version LIVE target and
  enumerated synthetic setup and writes. Prefer a separate disposable local
  development target so exercising the app cannot affect the fixed provider
  test target. Use only the reviewed setup for that target, including separate
  migration and fixture approvals if needed. Confirm its exact seven-migration
  history before starting the API. Do not repoint either existing fixture
  executable or loosen its fixed-target constraints. Pre-stage known synthetic
  actors, public catalog and times; if the setup or authority is missing, mark
  the corresponding scenario blocked rather than improvising. Arrange disposal
  of the isolated environment separately; do not truncate, reset or manually
  delete unrelated rows.
- [ ] Bind the API privately, retain startup guards and use synthetic accounts:
  an owner, another patient in the same clinic and a patient in another clinic.
  Configure an exact browser origin or use a private emulator as in
  [Flutter LIVE setup](../../mobile/README.md). Use a verified timezone, enabled
  doctor/service schedules, independent clients for races, and only local fake
  notification transports. Disable real SMS/SMTP and do not route any
  notification to an external provider. Do not enable public access or weaken
  TLS, authentication or tenant controls.

The later clinical-audit review overlay has eight PostgreSQL source
migrations and deliberately blocks these live workflows. The seven-migration
rescheduling candidate is the scope of this checklist; do not reuse its
approvals for the audit overlay.

## 2. Verify in the same revision

Confirm the Flutter LIVE client points at the approved API, not the demo.
Exercise the patient UI and use separately authenticated requests where two
actors are needed. Use fresh synthetic source/target slots per scenario. When
inspecting storage, use authorized read-only scoped observations; never report
patient fields, credentials, tokens, request UUID values, notification
recipients/bodies or raw database output.

| Check | Acceptance evidence |
| --- | --- |
| Happy path | In Flutter select an offered future slot for the owner's confirmed booking (including one that only overlaps its own old slot). Choose the slot and confirm. HTTP 200 returns a new ID; Flutter shows the new time, refreshes history and no longer shows the original as confirmed. The original is Cancelled, exactly one replacement is Confirmed, exactly one ledger row exists, and the old slot can be booked again if no other booking blocks it. The same UUID and instant replay to the same replacement, not to a second booking. |
| Auth and tenant isolation | No login yields 401; an authenticated wrong role yields 403; another patient or clinic gets 404 for both slot discovery and move. No unrelated appointment, ledger row or notification changes. A new move with the owner's unverified email is refused. |
| Definitive conflict and rollback | Have a separate patient book the destination after the owner's advisory slot list loads. A definitive move HTTP 409 clears the old choice and refreshes availability or asks the user to refresh history if there is no valid date/source. The rejected operation leaves the source Confirmed at the original time with its original notification state; no replacement, ledger or new outbox entries result. Do not confuse this business rejection with a lost response. |
| Ambiguity: 503 or lost response | Reproduce a lost response only in the private, authorized test environment (e.g. client loses the response after server commit) or observe a genuine 503. Flutter must keep the exact original selection/UUID in the same sign-in session and not treat 503 as refusal, success, or permission to submit a new move. Retry that same intent and reconcile authoritative history: it may find a committed move and return its replacement, or establish a definitive conflict, without duplicate writes. A synthetic transport failure checks the client path but is not proof of native PostgreSQL returning 503; mark the latter unobserved if it did not occur. Restart recovery is not covered by the current session-only UUID. |
| Two moves on one source | Submit concurrently from independent clients. For a shared UUID/start both calls resolve to one recorded replacement after replay. For different requests at most one moves the source; reconcile 503 with the same UUID before declaring outcomes. Verify exactly one ledger row and no extra replacements or notification entries. Do not accept two successful moves from one source. |
| Move competing with cancellation | Race both operations against the same confirmed original. If cancellation wins, no move ledger or replacement exists. If move wins, cancellation of the already-cancelled original may return its existing state but must not cancel the replacement or create a misleading original cancellation notice. Reconcile uncertainty before asserting a winner. |
| Move competing with booking | Use two independent clients for the same doctor destination, and repeat for the same patient's overlapping destination if fixtures support it. PostgreSQL must permit at most one overlapping confirmed interval for each protected key. If the move did not commit, the old booking and outbox remain unchanged. A pre-transaction conflict does not prove rollback after the source update: where approved instrumentation can observe a post-update failure, additionally verify that its rollback preserves the source and leaves no ledger/outbox writes. Otherwise leave that provider-specific fault case unverified. |
| Notifications | With real emitters disabled, track old pending confirmation/reminder entries and replacement entries via state/event/channel counts. Processing due old entries after the commit must suppress them; an old notification already accepted before commit cannot be retracted. No generic cancellation entry is generated for the move. The replacement gets one eligible confirmation per enabled channel, and an eligible SMS reminder only if sufficient lead time remains. Fake delivery points to the replacement time; replays cannot duplicate any entries. A failed move does not suppress the still-valid original's notices. |
| Ledger/guards | Confirm migration seven creates only its ledger/index, verifies both restrictive same-clinic FKs and the unique replacement key, and leaves the prior doctor/patient overlap exclusions in force. Same-patient linkage is enforced by the API, not independently by ledger FKs. Do not execute ad hoc DDL/DML for this inspection or claim database-level same-patient enforcement. |

## 3. Sign-off

- [ ] Record candidate SHA, platform, Flutter build, date/time, exact migration
  ID, completion or blockage per scenario, anonymized counts/status codes and
  whether transports were fake. Include safe test totals and evidence links.
  Never store raw logs/screenshots with identifiers, credential values or
  notification contents.
- [ ] Document failures and unexercised cases in `DEVELOPMENT_CHECKPOINT.md`
  and `HANDOVER.md`. Do not claim LIVE acceptance or production readiness
  until the required scenarios are independently verified. This gate does not
  authorize a deployment, merge, migration, fixture write or live transport.
