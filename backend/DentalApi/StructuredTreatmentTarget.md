# Routine tooth targeting groundwork

This internal validator is not an enabled procedure workflow or clinical advice.
It does not change any route, stored note, patient/visit audit, tariff, schema,
or provider gate.

- **Visit**: explicit scope, no tooth code (null only; empty or whitespace is
  not accepted).
- **Single tooth**: explicit scope, exactly two ASCII digits forming one
  routine FDI tooth identifier. Permanent quadrants 1–4 use positions 1–8;
  primary quadrants 5–8 use positions 1–5, for 52 possible identifiers.
- Omitted/unknown scope, malformed codes and inconsistent scope/code pairs
  fail validation. No normalization, patient-age inference, anatomic
  diagnosis, multi-tooth, oral-region, missing-tooth, or atypical-tooth
  designations are part of this validator. A valid code does not establish
  that the patient has that tooth or that a particular procedure applies.

The numbering subset is based on [ISO 3950:2016](https://www.iso.org/standard/68292.html),
[Figure 1 in the published standard preview](https://preview.sist.si/sist-preview/68292/d01f6acb32fc4566afd1d070f8e0e4c0/ISO-3950-2016.pdf),
with routine dentition background in the [FDI trainer's manual, pp. 14–15](https://www.fdiworlddental.org/sites/default/files/2021-09/Trainer_NOHP_EDU_Digital.pdf#page=14).
The application deliberately does not implement the standard's oral-area
designation system.

## Nonfinancial procedure draft

The internal `StructuredProcedureDraftService.ValidateAsync` takes a principal
already validated by clinic authentication, an appointment ID, a positive
service ID and the explicit target. It derives clinic, patient and clinician
from the active assigned doctor check on the started, confirmed encounter.
It reads only ID and label from an active, well-formed service in that clinic,
returning a read-only snapshot of those identities and validated target.
Unknown, inactive, other-clinic or malformed services and unavailable
encounters return no snapshot. No caller label, clinic, actor, seller, price,
tariff, version or payment field is accepted. It intentionally does not treat
a booking configuration's doctor/service assignment as clinical permission.

Today's `Service` row is a mutable booking catalog with estimated min/max
prices; it is not a clinic-approved clinical procedure revision, tariff
schedule or billing record. The returned label is the catalog label at the
time of this validation, not evidence of an approved price or an immutable
database record. There is no financial fallback, invented version, or route.

Before an enabled write: verify which services may be performed and by whom;
use authenticated clinic access and re-resolve the current actor, encounter,
catalog and target inside the signing unit of work. Define provider-safe
tenant linkage and persist immutable procedure identity/label plus the signed
clinical change and metadata-only audit atomically; failed audit must not
release or commit a record. Re-review history scope and the existing live gate.
Any financial extension separately needs an approved effective, immutable tariff
version and seller/rate/currency terms; never infer them from mutable estimates.

## Audited performed-procedure write draft

`PerformedProcedureService.SignAsync` is an internal service with no route.
It refuses to write except in a Testing host using in-memory SQLite. It
re-resolves the authenticated active assigned doctor, begun confirmed
same-clinic encounter and active service label inside one write transaction.
The service must be the encounter's booked service, with a current active
matching `DoctorServices` assignment; this deliberately does not infer
substitute services or wider clinical permission from booking data.

A signed item stores the server-resolved clinic-scoped visit, clinician and
service IDs, its service label at signing, the explicit target and UTC
timestamp, and the caller's nonempty visit-scoped replay UUID. The patient
link stays on the immutable visit identity. A retry with the same signer,
service and target returns only replay status, not the stored text; changed
target is refused, and current authority is checked again. Catalog renames
do not change the stored label. No price, revision, seller or payment data is
accepted, selected or stored.

The item and its metadata-only `TreatmentRecorded` audit event commit or
roll back together (the existing audit action is shared with signed notes).
Provider migrations are additive and use tenant links plus signer/assignment,
target and append-only guards; original notes and addenda are untouched.
PostgreSQL source SQL is checked offline only. The seven-migration live
safeguards remain closed and no persistent migration was run. Before any
clinician-facing route, review approved procedure permission, independent
audit/export and real target validation under separate approval. Billing
requires separate effective approved tariffs and is not inferred here.

## Performed-procedure synthetic HTTP acceptance

The first HTTP draft adds
`POST /api/clinical/appointments/{appointmentId}/performed-procedures`.
It is off unless `Features:PerformedProcedureAcceptance=true` is explicitly
set, the process is in Testing, and the active EF provider is SQLite using
an in-memory connection. Development, Production, PostgreSQL and file
SQLite are not enabled even with the flag. This is not a rollout mechanism.

The real clinic Bearer/Doctor path validates identity before the transaction.
The route ID supplies the visit. The JSON body is capped at 1 KiB and takes
only `requestId` (nonempty UUID), `serviceId` (positive JSON integer),
`scope` (`Visit` or `SingleTooth`) and `toothCode` (null for Visit or one
canonical routine FDI code for SingleTooth). Caller-provided clinic,
patient, clinician, label, financial or route identity fields are refused.
The service still rechecks booked service, current qualification and
assignment within its atomic audit/write transaction.

Successful 201 returns appointment ID, request UUID, authoritative service
ID/label, scope, optional tooth code and signed UTC only; it is not a
payment or receipt. Exact retries return 204 with no result body; changed
target for the same visit UUID returns 409. Unknown or unavailable visits or
qualifications return 404, malformed input 400 (declared oversize 413), and
storage/audit failure generic 503. Action responses use no-store.
No new schema, billing or deployment is included.

### Same-encounter signed procedure page

`GET /api/clinical/appointments/{appointmentId}/performed-procedures?page=1&pageSize=20`
uses the same off-by-default flag, Testing environment, in-memory SQLite and
clinic JWT Doctor gates. The doctor must currently be active and assigned to
the begun confirmed same-clinic encounter; only rows that doctor originally
signed are included. New assignees cannot inherit the previous author's rows.
A successful authorized empty page is still audited before return.

Pages return only `page`, `pageSize`, `hasMore`, and clinical `items`:
request UUID, authoritative signed service ID/label, target scope, optional
tooth code and signed UTC. The label comes from the immutable signed item,
not the current catalog. There are no totals, unrelated identifiers,
finances, note text or patient-wide queries. Ascending signed UTC and UUID
give deterministic order for a fixed dataset. Page 1–1,000 / size 1–50 only.
Each release must first commit a metadata-only `HistoryRead` event.
Unassigned or foreign users receive no items or access audit. Failure to
insert or commit audit returns generic 503 with no clinical data. Responses
from the action are no-store. This does not approve new read roles or
production data access.
