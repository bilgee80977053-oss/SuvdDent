# Test-only, anchor-bound patient visit history

`GET /api/clinical/appointments/{appointmentId}/patient-history?from=2026-11-01&until=2026-12-01`
is a synthetic acceptance route, not a general patient history API or an approved
production assignment policy. It is off unless both
`Features:PatientHistoryAcceptance` and `Features:PerformedProcedureAcceptance`
are explicitly true, with Testing and in-memory SQLite. The Clinic JWT Doctor
must currently be active and assigned to the begun, confirmed anchor encounter.

The service derives the patient and clinic from the authorized anchor and has no
patient or clinic ID input. A listed visit must belong to that same patient and
clinic, be currently accessible under the existing assigned-encounter rule and
contain at least one performed procedure signed by this same doctor. Appointment
identity and start UTC are the only item fields. There are no patient identifiers,
other doctors' rows, clinical text, billing data or count totals. An encounter
appearing here does not grant any new rights to its clinical content.

Required ISO UTC dates are start-inclusive and end-exclusive; period is 1–31
days. The newest start and then largest appointment ID come first. Page 1–100
and size 1–20 are accepted (defaults 1 and 10). An additional row is used only
to determine `hasMore`; the last supported page does not promise another page.
Order is deterministic for an unchanged dataset.

Each released page requires one committed metadata access event for the anchor and
for each distinct included visit; events from the same page share a server
generated correlation UUID. An empty page still requires its anchor event. Any
audit insertion or transaction failure blocks the whole response and returns
generic 503 with no data. Action responses are no-store. No migrations, clinician
UI, provider rollout or real patient data are enabled by this slice.
