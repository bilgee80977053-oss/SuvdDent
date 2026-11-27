# Clinical treatment and history acceptance criteria

Status: development contract; not release approval. This branch does not expose
clinical history and adds no clinical tables, migrations, or audit storage.

## First slice: verified encounter scope

- Use the clinic authentication scheme with database-backed claims, never the
  Platform identity scheme or tenant/patient/clinician IDs in request bodies.
- Only an active doctor assigned to the encounter, with an active doctor profile
  and active same-clinic patient, can obtain the internal clinical encounter
  scope. The first workflow requires a confirmed appointment whose scheduled
  start has passed. It does not grant access for future/cancelled appointments,
  unassigned staff, administrative or financial roles, or other clinics.
- This is encounter scope, not permission to browse all patient records. A
  future history reader must separately define its record scope, assignment
  lifecycle and retention. No emergency access, pre-visit access or proxy
  access is implied; any such expansion requires separate policy review.
- The current self-service profile remains a demographic projection. It neither
  emits nor accepts clinical fields, and its responses request no caching.

## Acceptance before exposing a clinical workflow

| Capability | Required check |
| --- | --- |
| Clinical encounter entry | Signed clinic Doctor identity with active assignment; cancelled, cross-clinic, unassigned and non-clinical role access cannot return a record. No client-owned identity overrides. |
| Audit first | Persist append-only metadata for successful clinical reads and writes: actor, clinic, patient/encounter and action IDs, UTC time, and correlation reference. Never record note text, allergies, contacts, tokens or medical payloads in the audit trail. A failed audit does not release history; clinical writes commit together with their audit event. |
| Treatment record | Link records to the same-tenant encounter, patient and clinician with database constraints. Validate procedure and approved tooth-notation values; snapshot the accepted procedure version and any tariff used. Treatment edits are amendments that preserve previous signed entries. Clinical content does not accept payment state. |
| Patient history | Start with the verified encounter scope. Return only explicitly allowed fields, bounded and ordered results and no-store responses. Treatment histories across clinics, patient self-service history and export remain unavailable until policy and access auditing are separately defined and tested. |
| Integrity | Exercise synthetic create/read/amend flows, invalid and cross-tenant foreign keys, concurrency/retry behavior, audit rollback and HTTP role separation. Review independent SQLite and PostgreSQL model histories. PostgreSQL runs require the separate authorized target/workflow. |
| Billing handoff | Expose only the procedure/tariff snapshot required for invoicing, not clinical notes or diagnosis. A treatment record or estimate is not a payment or receipt. |

Next implementation slice: define and add durable minimal audit storage with
provider-specific constraints and isolated rollback tests. Use the scope service
inside the write transaction before committing an audited clinical encounter.
