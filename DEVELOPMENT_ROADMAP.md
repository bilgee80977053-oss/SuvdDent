# Development roadmap

Status: handover snapshot for `feature/suvd-dent-backend-completion`, 2026-11-27.
This is a development branch, not production release approval.

## Current implementation

- ASP.NET Core 10 multi-tenant API with clinic and Platform identities,
  database-backed claim checks, role enforcement and tenant-scoped operations.
- Patient profiles, doctor profiles, scheduling/availability, Mongolia clinic
  location/timezone verification, notification preferences/outbox, and
  phone-verification challenges.
- Patient booking endpoints for service/doctor/slot discovery, create, list and
  detail. Booking validates eligibility and snapshots, uses idempotent request
  IDs, and handles provider concurrency conflicts. A patient can cancel their
  own future appointment in the API and Flutter LIVE history; cancellation
  preserves the record and releases the occupied slot.
- Appointment integrity is enforced in SQLite by triggers and in PostgreSQL by
  composite foreign keys and partial GiST exclusion constraints. The PostgreSQL
  constraint verifier accepts the one trailing `~` used by the EF-generated
  truncated FK identifier, while rejecting other punctuation.
- Flutter has a preserved synthetic DEMO journey and an explicitly configured
  one-clinic LIVE booking flow. A clinic ID must come from an authorized
  development operator; there is no public clinic discovery API.
- An appointment-linked payment estimate and service tariff administration
  exist, but no payment settlement, legal receipt, or real charge is performed.

## Priorities

### P0 — Complete authorized PostgreSQL validation

1. Have the database operator confirm an isolated, disposable PostgreSQL 18
   test target and independently authorize the runner's read-only preflight.
2. Review generated SQL and existing data preconditions for each unapplied
   migration. Run no migration until each write has separate explicit approval.
3. If approved, apply the exact pending migration range through the guarded
   rehearsal workflow, then verify the exact migration history and required
   constraints. Obtain separate approval for the write-capable PostgreSQL
   regression suite. Record live results independently from offline tests.
4. Prefer the verified offline PowerShell helper run on the review branch and
   preserve the workstation's script policy. An offline pass never substitutes
   for operator-authorized PostgreSQL validation.

### P1 — Complete booking operations and client integration

1. Run Flutter `analyze` and `test`, then perform an authorized development-only
   end-to-end booking check after the backend environment is approved.
2. Define the first clinic onboarding flow and permissions. Evaluate an
   operator-assisted listing separately from patient discovery and clinic
   self-service; do not probe arbitrary IDs.
3. Verify booking, cancellation and the atomic rescheduling review branch with
   the same Flutter and backend revision on an approved development target.
   The [LIVE acceptance checklist](backend/DentalApi/ReschedulingAcceptance.md)
   records separate target approval and the remaining provider checks.
4. Review the notification outbox worker, SMS/email provider configuration,
   retries and observability before relying on delivery in a live workflow.

### P2 — Clinical treatment and patient history

1. Review the draft overlay's encounter-scoped access and metadata-only,
   append-only audit storage. The overlay exposes no clinical endpoint.
2. Review the [first immutable encounter-note draft](backend/DentalApi/EncounterTreatmentApi.md).
   It is gated off by default and refuses production, persistent SQLite and
   PostgreSQL. Review retention and the separate PostgreSQL migration/validation
   gate before any live trial.
3. Review signed encounter addenda and bounded metadata-only discovery for
   one encounter under the same gate. Define approved
   tooth notation, historical procedure/tariff snapshots, and scoped
   longitudinal medical history using synthetic data. Separate clinical
   and billing access.

### P3 — Financial, clinic administration and production readiness

1. Obtain and version clinic-approved tariffs; add effective dates, approvals and
   a tamper-resistant tariff audit trail.
2. Design a payment ledger/invoice snapshot, payment gateway integration,
   verified callbacks, idempotency, refunds/reconciliation and receipt lifecycle.
   Never infer settlement from the client or label the estimate as a receipt.
3. Specify treatment workflow acceptance criteria before estimating functional
   completion: which roles can add, view or amend a record, how it links to an
   appointment, and what must be audited. Do not treat an appointment status as
   a treatment record.
4. Complete authorization review, privacy retention/deletion policy, audit logs,
   deployment/backup/restore/monitoring plans, accessibility and clinical-content
   review before production approval.
5. Keep production deployment and real-data migration as a separately authorized
   release process.

## Explicitly out of scope until approved

- No production deployment or merge to `main`.
- No live database migration, schema reset, destructive data operation or
  modification of real patient records.
- No real payment, receipt, SMS, or email validation using patient data.

## Billing acceptance gates for the next non-production review

- An estimate is display-only. Any payable invoice must be separately issued
  from approved, effective pricing for the verified clinic and appointment.
  Keep the applied revision, approval reference and exact monetary units.
- A transaction verification, candidate or reservation is not payment. Only
  a trusted committed settlement may be considered, and its clinic,
  appointment, invoice and amount must all match. Check duplicate and
  concurrent transaction claims, ledger rollback and unknown commit outcomes.
- A receipt prerequisite check is not issuance. Before adding issuance,
  confirm the legal issuer, required content, numbering, void/reversal and
  tax rules, and review durable uniqueness, authorization and audit. No
  rule, provider or amount is presumed here.
- Keep operator-approved PostgreSQL, real merchant integrations, real
  patient data and release deployment outside synthetic acceptance.

- The internal draft review breakdown now provides an exact, ordered,
  non-payable projection with generic labels. Before exposing it, design
  authorization tied to a stored draft and its owner, audit and no-store
  behavior. Separately approve the effective tariff, invoice lifecycle and
  display labels before producing an issued or payable document.
- The Flutter My Bookings acceptance composition can select one booking and
  show a synthetic nonpayable review. Keep that composition release-disabled
  until a real stored review, verified owner authorization, access auditing,
  and approved nonclinical labels are available. The booking price estimate
  remains independent; do not use it as an invoice review fallback.

## 2026-11-28 review and acceptance order

Review #6, #7 (including #5) and then #8–#16 in order. Keep validation-only
#17 unmerged and review the CI trigger change in #18 independently. Gate final
booking and clinical acceptance on paired Flutter/DentalApi loopback tests
using synthetic data. Distinguish encounter-only records from longitudinal
history and verify authorization and committed audit before release. Once
the clinical milestones meet acceptance, add provider-neutral invoice
snapshots, verified settlement, receipts and clinic administration. Keep
PostgreSQL live, production deployment and main merges outside this approval.

## Synthetic acceptance evidence, 2026-11-28

- Atomic patient appointment rescheduling: the real Flutter widget and API
  passed on #23, including stale-slot rollback and same-key replay.
- Clinician procedure confirmation and reviewed history: actual HTTPS
  passed on #24; the native clinician confirmation panel passed separately
  on #29. Immutable note/addendum creation and audited reads passed on #28.
- Continue by exercising the complete opt-in clinician entry from verified
  sign-in to server-assigned selection, procedure and note confirmation on
  one exact synthetic stack, then review workflow handoff and privacy scope.
- Billing #22/#27 covers arithmetic/evidence only. Approved tariff versions,
  invoice issuance, atomic payment ledger, legal receipts, and broader
  clinic administration remain open. Validate runtime PostgreSQL and real
  device/release readiness only with separate authorization.

## Integrated release-review gate

Review the combined #45 + #43 + #23 + #25 + #38 candidate as one synthetic
gate after the stacked foundations. Preserve both the patient reschedule and
clinician HTTPS acceptance runs, plus offline backend, Flutter, and PowerShell
verification on that exact tree. Green results do not imply persistent
PostgreSQL acceptance, device release readiness or permission to deploy.
The clinical entry and invoice review remain isolated; invoice issuance, real
settlement, legal receipt handling and other release prerequisites are separate.

The supplemental draft keeps both distinct loopback checks and the nonpayable
review release-disabled, and pins reviewed migration source metadata only.
Keep the operational PostgreSQL rehearsal runner at its reviewed seven-ID
chain until a separate clinical rehearsal is reviewed and authorized.

- The draft review fixture exercises the exact approved snapshot projection
  with normal booking ownership on a synthetic host. It is not a release
  endpoint or approval to introduce real tariffs, invoice issuance,
  provider calls, payments or receipts.
