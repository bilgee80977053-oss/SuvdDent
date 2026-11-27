# Patient appointment booking API

All routes below require the existing clinic JWT and Patient role. Clinic and
user identity come exclusively from validated claims. Platform tokens cannot
substitute for clinic tokens.

| Method | Route | Purpose |
| --- | --- | --- |
| GET | `/api/appointments/services` | Active services in the token clinic |
| GET | `/api/appointments/doctors?serviceId=51` | Active, published doctors with active Doctor users and an active association to the selected service |
| GET | `/api/appointments/slots?doctorProfileId=31&serviceId=51&date=2026-11-02` | Advisory available slots for one clinic-local date |
| POST | `/api/appointments` | Atomically confirm an appointment |
| POST | `/api/appointments/{id}/cancel` | Cancel an own future booking and free its slot |
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
storage records do not block time. The database enforces these intervals in
addition to the availability and booking checks: PostgreSQL exclusion
constraints and SQLite insert/update triggers cover direct and concurrent
writers. Cancellation marks a row cancelled and releases its interval.
All snapshots come from current trusted database configuration. The whole-minute
grid avoids PostgreSQL/SQLite fractional start-time precision differences.

Availability is advisory and excludes the authenticated patient's conflicts too.
It runs in a provider-aware transaction so clinic policy and multiple queries share
a consistent view; SQLite briefly takes a write reservation despite making no
writes. Blocking appointments are fetched in one bounded date-range query.
POST repeats validation and overlap checks regardless of earlier availability.

## Rescheduling slot discovery

`GET /api/appointments/{id}/reschedule-slots?date=YYYY-MM-DD` returns the
same slots DTO as ordinary booking, for the existing appointment's doctor
and service. No clinic, patient, doctor or service override is accepted.
Only the authenticated owner's future confirmed appointment qualifies.
Invalid input returns 400, missing or inaccessible appointments return 404,
and a started or cancelled appointment returns 409.

The query ignores only the source appointment's overlap and omits its
current start time. Other confirmed doctor and patient bookings still block
slots. Results are advisory. This endpoint does not reserve a slot, change
an appointment, or enqueue a notification. Flutter LIVE uses this advisory list before submitting a move.

## Atomic rescheduling

`POST /api/appointments/{id}/reschedule` requires the authenticated patient
and JSON `{"startUtc":"2026-11-30T09:30:00+08:00","requestId":"<uuid>"}`.
The clinic, patient, doctor and service derive from claims and the stored
appointment. A new move requires an active patient with verified email and
a future confirmed source. The clinic must have a verified timezone; all
eligibility, schedules and overlap checks run again at confirmation.

On success HTTP 200 returns the replacement appointment (a new ID); the
source remains in history as `Cancelled`. The source status update,
replacement insert, clinic-scoped move ledger and new outbox entries use one
transaction. No separate cancellation message is queued; queued old
confirmation/reminders are suppressed before delivery by the existing worker.
Notifications already delivered before the move cannot be withdrawn.

Reusing the same request UUID and instant for that source returns its recorded
replacement, including its current status if later cancelled or moved again.
Another request/instant for a moved source returns 409. Retries are never
applied as new writes when that result can be found. A rejected or failed
uncommitted move preserves the original booking and notification state.
A transport error or transient database 503 is not proof of failure; retry
the same UUID or refresh the authoritative history. A 409 returned by the
move endpoint is a definitive conflict, not an unresolved provider error. Flutter retains the retry UUID for that choice in the
current sign-in session.

The ledger has restrictive compound clinic foreign keys to both appointments.
The service requires the same owner for the two rows; the ledger does not
independently enforce matching patient IDs in direct database writes.
SQLite and PostgreSQL retain their existing overlap enforcement. Both
migrations only add the ledger table and its unique replacement index.

## Concurrency and request identity

SQLite uses DatabaseCompatibility's non-deferred write transaction, obtaining its
write reservation before eligibility/overlap reads. PostgreSQL uses Serializable
isolation. The provider-specific database overlap guards are the final integrity
boundary; the service's transactional checks also produce early conflicts and keep
availability behavior consistent. Moves lock the source row for update on PostgreSQL and reuse the
SQLite write reservation. Existing booking creation does not lock a source row.

SQLSTATE 23505, 23P01, 40001 and 40P01 and SQLite busy/locked/unique/trigger
conflicts return safe messages. Creation returns 409; a move that cannot yet
be determined returns 503 unless a fresh read finds a committed move. Failed transactions dispose/roll back before
any duplicate lookup.
There is no automatic transaction retry. A single fresh lookup can recognize a
concurrently committed duplicate.

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

## Cancellation contract

Send a POST with the appointment ID in the route and no body. The caller's
validated clinic and user determine ownership. Nonpositive IDs return 400;
inaccessible or missing appointments return 404. A confirmed appointment that
has already started returns 409. Successful cancellation returns 200 with
the existing appointment response and status `Cancelled`. The record remains in
history and its interval no longer blocks slots. Repeating a successful
cancellation returns that same status without queuing a duplicate event.
An eligible notification outbox entry and the status change are committed together.
Client reconciliation after an uncertain outcome can use the existing detail GET.
This endpoint does not reschedule an appointment.

## Validation and limits

AppointmentApiTests use synthetic in-memory SQLite, real JWT validation, independent
HTTP request connections, concurrent booking attempts, and an injected commit
failure. AppointmentProviderTests suppress all PostgreSQL connection opens and
check Serializable selection and SQLSTATE handling. Two opt-in
PostgreSqlAppointmentTests coordinate independent snapshots before overlap reads,
assert one committed booking and test duplicate replay. They reuse the existing
approved target guard and require the appointment migration to have already been
applied by an authorized operator. Tests never
apply PostgreSQL migrations or reset schema. Cleanup removes only generated request
keys in an ownership-checked synthetic clinic, followed by existing fixture cleanup.

Live PostgreSQL behavior is not established by offline tests. Leave
NEXORA_PG_TEST_CONNECTION absent for normal offline validation. The opt-in target
must satisfy all existing disposable database safety checks; do not use a shared
or production database. No database connection is configured by this document.

The relationship and database-overlap migrations are documented in
[appointment persistence](AppointmentPersistence.md). PostgreSQL startup
migration guard remains enabled. Flutter LIVE uses the cancellation endpoint
from appointment history; same-revision end-to-end verification with an approved
PostgreSQL target is separate from the offline API and widget tests. Rescheduling,
payments and treatment history are outside this API. Notifications remain
transactional outbox work and are not sent before commit.
