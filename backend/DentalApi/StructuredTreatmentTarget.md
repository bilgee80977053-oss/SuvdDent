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
