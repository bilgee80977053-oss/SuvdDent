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
  IDs, and handles provider concurrency conflicts.
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
4. Execute the offline PowerShell helper tests in an approved environment; the
   current workstation's `Restricted` policy prevents script execution.

### P1 — Complete booking operations and client integration

1. Run Flutter `analyze` and `test`, then perform an authorized development-only
   end-to-end booking check after the backend environment is approved.
2. Add a clinic discovery/onboarding workflow only after defining clinic
   authorization and privacy requirements; do not probe arbitrary IDs.
3. Define booking cancellation/rescheduling and notification delivery lifecycle,
   with ownership checks, idempotency, auditability and regression tests.
4. Review the notification outbox worker, SMS/email provider configuration,
   retries and observability before relying on delivery in a live workflow.

### P2 — Financial and production readiness

1. Obtain and version clinic-approved tariffs; add effective dates, approvals and
   a tamper-resistant tariff audit trail.
2. Design a payment ledger/invoice snapshot, payment gateway integration,
   verified callbacks, idempotency, refunds/reconciliation and receipt lifecycle.
   Never infer settlement from the client or label the estimate as a receipt.
3. Complete authorization review, privacy retention/deletion policy, audit logs,
   deployment/backup/restore/monitoring plans, accessibility and clinical-content
   review before production approval.
4. Keep production deployment and real-data migration as a separately authorized
   release process.

## Explicitly out of scope until approved

- No production deployment or merge to `main`.
- No live database migration, schema reset, destructive data operation or
  modification of real patient records.
- No real payment, receipt, SMS, or email validation using patient data.
