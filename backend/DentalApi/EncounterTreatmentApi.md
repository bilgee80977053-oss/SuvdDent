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

## Add an immutable signed note to the same encounter

`POST /api/clinical/appointments/{appointmentId}/treatment/addenda`

```json
{ "requestId": "8855c576-1777-49ec-8a93-cfe1c1239f67", "note": "Synthetic addendum for review." }
```

An addendum requires an existing original signed note and current assignment
to the same encounter. This draft allows only the original signer to add or
read addenda. It uses the same authentication, no-store and synthetic in-memory
opt-in gate as the original note. The text/body limits and normalization are
also unchanged. Keep the same client-generated nonempty UUID for a retry;
it is scoped to the clinic and encounter. Creation returns 201 with a Location
and no note body. Exact normalized replay returns 204 with no second audit event
or addendum; a different note with the same key returns 409. Each distinct
successful write commits a metadata-only `TreatmentAmended` audit event in
the same transaction. Neither the original record nor another addendum is
replaced. No user-input correlation is copied to the audit trail.

`GET /api/clinical/appointments/{appointmentId}/treatment/addenda/{requestId}`
retrieves exactly one addendum by its encounter-scoped UUID, only after a
successful read audit. It returns only `requestId`, `note` and `signedUtc`.
Failed audit returns generic 503 without clinical text.

`GET /api/clinical/appointments/{appointmentId}/treatment/addenda?page=1&pageSize=20`
discovers metadata for this single encounter under the same current original
signer authorization and in-memory-only gate. Page numbers are 1–1,000 and
page sizes are 1–50. The response contains `page`, `pageSize`, `hasMore` and
`items`. Each item contains only `requestId` and `signedUtc`, never note text.
Pages are ordered by signed UTC then by request UUID, ascending; this order
is stable for a fixed dataset. The last supported page does not advertise
another page. Each returned page, including an authorized
empty page, requires a successful audit transaction. Audit failure returns
generic 503 without the page. This route is no-store and is not a general
history, patient-wide search, or update/delete route.

## Storage and remaining acceptance

The original and each addendum stay immutable. Their provider migrations
add separate tables, tenant-scoped indexes and mutation/signer guards without
rebuilding existing tables. The database refuses an unassigned signing identity
and ordinary mutations, including SQLite replacement and PostgreSQL truncation.
Application-role review, retention and tamper-resistant operational controls
remain open. The earlier seven-migration live workflow remains closed
to this ten-migration PostgreSQL source branch. The addenda table references
the original by clinic and encounter while adding its own signer and
immutability guards; historic migrations are unchanged.

Consent/retention review, emergency access, chronological medical
history, clinical procedure and tooth-notation validation, patient access,
Flutter UI and all billing/receipts remain out of scope for this draft.
