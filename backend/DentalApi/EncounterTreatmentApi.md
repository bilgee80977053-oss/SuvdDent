# Assigned encounter treatment note (draft, synthetic review only)

The first treatment API signs one immutable plain-text note for one started,
confirmed encounter. It is not a clinical history list, an approved medical
record product, a cryptographic signature, or a billing feature.

Both routes are off by default. They require the explicit
`Features:ClinicalTreatment=true` flag, an ASP.NET Core Development or Testing
host, and an in-memory SQLite connection. A persistent SQLite file and every
PostgreSQL connection are refused even when the flag is set. Use synthetic data
only. Do not run this draft against any persistent database: the route
gate does not change the host's existing SQLite startup migration behavior.
No live or production validation is authorized by this branch.

## Request and authorization

`POST /api/clinical/appointments/{appointmentId}/treatment`

```json
{ "note": "Synthetic observation for an isolated test." }
```

The server accepts one text field only, with at most 2,000 UTF-16 code units and
a 16 KiB request cap. Blank notes and unsupported control characters are
rejected, line breaks normalized to LF, and surrounding whitespace trimmed.
There is no client-set clinic, patient, author, timestamp, payment, or
procedure field.

Both this route and the GET below require the clinic Bearer scheme and Doctor
role. The server resolves an active same-clinic patient, an active profile
assigned to the encounter, an active Doctor user, and a confirmed visit whose
start has passed. The clinic-scoped encounter supplies patient, doctor profile
and booked service identity. Missing, unassigned, not started, cancelled and
foreign encounters are indistinguishable to a signed-in doctor (404).
Other authentication schemes and non-doctor roles do not receive clinical data.

Successful POST returns 201 and an encounter-specific Location header, without
copying the signed note into the response. An existing record returns 409 and
is not replaced. The initial record cannot be modified or deleted. A failed
audit/write returns a generic 503 and does not indicate that the record was
signed. The note and event commit together; do not assume the outcome if a
response itself was lost.

`GET /api/clinical/appointments/{appointmentId}/treatment` returns only
`appointmentId`, `note` and `signedUtc` to the same still-authorized signing
clinician. A metadata-only read audit must commit before the server releases
the response. Audit failure returns generic 503 without the note. Route
responses containing protected data are marked `Cache-Control: no-store`.

## Storage and remaining acceptance

The original record stays immutable. Provider migrations create only
`TreatmentRecords`, a tenant-scoped signer index and provider-specific
triggers. The database refuses an unassigned signing identity and ordinary
mutations, including SQLite replacement and PostgreSQL truncation. The
application application-role review, retention and tamper-resistant operational
controls remain open. The earlier seven-migration live workflow remains closed
to this nine-migration PostgreSQL source branch.

Amendments, consent/retention review, emergency access, chronological medical
history, clinical procedure and tooth-notation validation, patient access,
Flutter UI and all billing/receipts remain out of scope for this draft.
