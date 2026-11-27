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
