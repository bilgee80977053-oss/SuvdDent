# Patient appointment booking API (University MVP, Step 3)

All routes below require the existing clinic JWT and Patient role. Clinic and
user identity come exclusively from validated claims. Platform tokens cannot
substitute for clinic tokens. Existing endpoints and contracts are unchanged.

| Method | Route | Purpose |
| --- | --- | --- |
| GET | `/api/appointments/services` | Active services in the token clinic |
| GET | `/api/appointments/doctors?serviceId=51` | Active, published doctors with active Doctor users and an active association to the selected service |
| GET | `/api/appointments/slots?doctorProfileId=31&serviceId=51&date=2026-11-02` | Advisory available slots for one clinic-local date |
| POST | `/api/appointments` | Atomically confirm an appointment |
| GET | `/api/appointments/me?page=1&pageSize=20` | Own bookings, newest start then ID first |
| GET | `/api/appointments/{id}` | One own booking; inaccessible IDs return 404 |

The existing authenticated `/api/Doctors` catalog remains reusable. New selection
DTOs omit private user/contact fields. The existing public service endpoint is
preserved; the new booking service catalog always uses the JWT clinic rather than
a query-supplied clinic ID.

## Creation contract

```json
{
  "doctorProfileId": 31,
  "serviceId": 51,
  "startUtc": "2026-11-02T01:15:00Z",
  "requestId": "d0f8eb40-2587-4cc2-a35a-23bfe410a3c8"
}
```

`startUtc` is an ISO timestamp with seconds and an explicit `Z` or `+/-HH:mm`
offset. Offset-free input is rejected. Equivalent offsets identify the same
instant. Identifiers must be positive and RequestId must be a nonempty GUID.
The server does not accept authoritative clinic/patient IDs, duration, buffers,
pricing or status. Unknown JSON properties do not grant authority.

A new booking returns 201 with a Location header. Response fields are `id`,
`doctorProfileId`, `serviceId`, `startUtc`, `endUtc`, `occupiedStartUtc`,
`occupiedEndUtc`, `clinicTimeZoneId`, `status` and `createdAtUtc`. Times are UTC
with an explicit offset. No patient notes or private user information are returned.
List responses contain `page`, `pageSize`, `items`; page is 1-100000 and pageSize
is 1-100 (default 20). Empty pages are valid; no total count is promised.

## Validation and slot rules

Booking rechecks the active Patient user, nonempty verified email, active linked
patient profile, verified clinic timezone (including active Director verifier),
active service, active published doctor and Doctor-role user, active doctor-service
association and working hours inside its transaction. Missing/inaccessible
profiles or resources return 404; unverified email returns 403; an unverified
clinic timezone returns 409. Existing authentication rejects inactive/revoked users.

Dates range from clinic-local today through today + 30 days, inclusive. Treatment
starts use a local-midnight-anchored 15-minute grid with zero seconds/fractions.
Starts at or before the current instant are unavailable. The entire occupied
interval (before buffer + treatment + after buffer) must fit one working-hours
interval on that date. Split shifts are not bridged. EndMinute 1440 denotes the
following midnight. Invalid/ambiguous local endpoints and intervals whose UTC
elapsed lengths differ from configured minutes are omitted/rejected, without
choosing a timezone offset by guesswork.

Confirmed doctor occupied intervals and confirmed patient treatment intervals
block slots. Half-open overlap means `existingStart < proposedEnd &&
proposedStart < existingEnd`; exactly touching boundaries are allowed. Cancelled
storage records do not block time; cancellation is not implemented by this API.
All snapshots come from current trusted database configuration. The whole-minute
grid avoids PostgreSQL/SQLite fractional start-time precision differences.

Availability is advisory and excludes the authenticated patient's conflicts too.
It runs in a provider-aware transaction so clinic policy and multiple queries share
a consistent view; SQLite briefly takes a write reservation despite making no
writes. Blocking appointments are fetched in one bounded date-range query.
POST repeats validation and overlap checks regardless of earlier availability.

## Concurrency and request identity

SQLite uses DatabaseCompatibility's non-deferred write transaction, obtaining its
write reservation before eligibility/overlap reads. PostgreSQL uses Serializable
isolation. Every booking reads the doctor/patient overlap predicate before inserting;
Serializable snapshot isolation detects competing absent-row reads and inserts.
No additional advisory lock is needed for this create-only path. The protocol
requires all future booking writers to preserve these transactional predicates;
ordinary unique indexes do not prevent arbitrary overlapping time intervals.

SQLSTATE 23505, 40001 and 40P01 and SQLite busy/locked/unique conflicts return safe
409 messages. Failed transactions dispose/roll back before any duplicate lookup.
There is no automatic transaction retry. A single fresh lookup can recognize a
concurrently committed duplicate; if it cannot yet be recognized, return 409.

For the same authenticated clinic/patient and RequestId, matching doctor, service
and start instant return the original appointment with 200, preserving its original
snapshots even if configuration has since changed. Different details return 409.
Replay still requires an eligible active patient and verified email, but does not
revalidate current schedules or timezone for an already completed booking. Clients
should retain a RequestId across retries of the same intent.

Malformed input returns 400, missing authentication 401, wrong role 403,
inaccessible resources 404 and unavailable times/conflicts 409. New domain messages
use Mongolian. Existing framework validation and global exception handling remain
in force; database details are not returned to clients.

## Validation and limits

AppointmentApiTests use synthetic in-memory SQLite, real JWT validation, independent
HTTP request connections, and an injected commit failure. AppointmentProviderTests
suppress all PostgreSQL connection opens and check Serializable selection and
SQLSTATE handling. Two opt-in PostgreSqlAppointmentTests coordinate independent
snapshots before overlap reads, assert one committed booking and test duplicate
replay. They reuse the existing approved target guard and require the appointment
migration to have already been applied by an authorized operator. Tests never
apply PostgreSQL migrations or reset schema. Cleanup removes only generated request
keys in an ownership-checked synthetic clinic, followed by existing fixture cleanup.

Live PostgreSQL behavior is not established by offline tests. Leave
NEXORA_PG_TEST_CONNECTION absent for normal offline validation. The opt-in target
must satisfy all existing disposable database safety checks; do not use a shared
or production database. No database connection is configured by this document.

No schema changes or migrations are introduced. PostgreSQL startup migration guard
remains enabled. Flutter integration, email/outbox delivery, cancellation,
rescheduling, payments and treatment history are outside this step. No message is
sent before or after booking; later email work must run only after commit.
