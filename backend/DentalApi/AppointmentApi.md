# Smart Appointment Booking, phase 1

## Safety and patient creation

This phase adds booking and read APIs, not slot discovery, cancellation,
rescheduling, DoctorTimeOff, appointments for dependants or clinical-history sharing.
PlatformAccount identity and TokenVersion come only from the Platform JWT and are
rechecked inside the transaction. Request AccountId/PatientId never choose a card.

A trusted existing ClinicPatientLink is reused only for its active local Patient.
When no link exists, the response is 409 PATIENT_CREATION_CONSENT_REQUIRED unless
ConsentToCreateNewPatient=true. The UI must present an explicit opt-in explaining
that a NEW card will be created at the selected clinic, sharing account FullName
and verified phone (if any). Do not preselect consent or imply an existing card
will be recovered. Email, medical notes, allergies, DOB, address and other clinics'
records are not copied. Unverified/absent phone is stored as an empty string, which
the current Patient schema supports; email-only accounts can still book. This does
not claim phone ownership or grant access to any old chart.

The service never searches old cards by name, email or phone. Consequently duplicate
local cards for the same person are possible and are safer than an unverified link.
Reconciliation requires a separate verified staff workflow in a later phase. A new
card has UserId=null; existing Patient/User tenant FKs stay intact. Patient, link,
consent audit and appointment commit in one transaction; a failure rolls all back.
The server records UTC consent time and policy version new-local-patient-v1 on the
first appointment which creates a card. Existing links do not require new creation
consent and inactive cards are not silently reactivated or replaced.

## API

All /api/platform routes below require the PlatformAccount policy:
- GET /api/platform/clinics/{clinicId}/booking-options?doctorProfileId=31&serviceId=51
- POST /api/platform/clinics/{clinicId}/appointments
- GET /api/platform/me/appointments?skip=0&take=50
- GET /api/platform/me/appointments/{id}

Options returns verified clinic timezone/Version, Doctor Version, duration, buffers
and local weekly working windows. It is NOT a free-slot response or a reservation.
No API claims a time is available based only on working hours. POST performs the
current authoritative conflict checks; a successful options response can become
stale. Use fresh versions and ask the user to review changes after a conflict.

POST requires Idempotency-Key: a nonzero UUID. Example body (replace IDs, versions
and time using the selected clinic's configuration):

```json
{
  "doctorProfileId": 31,
  "serviceId": 51,
  "startUtc": "2026-11-02T09:10:00+08:00",
  "clinicVersion": "11111111-1111-1111-1111-111111111111",
  "doctorVersion": "22222222-2222-2222-2222-222222222222",
  "consentToCreateNewPatient": true
}
```

The client supplies no EndUtc, duration, buffer, status or account/patient authority.
Start requires an explicit offset, minute precision and a future instant within
365 days. The server computes end and occupied start/end from DoctorService.
POST first success=201, exact idempotent replay=200, malformed request=400,
wrong/missing authentication=401, other-owner booking=404, ineligible resources=404,
stale versions/conflicts/consent needed=409. Error bodies contain errorCode. A busy
SQLite writer returns BOOKING_BUSY; retry only deliberately with the SAME key and
payload. A key reused for another normalized payload returns IDEMPOTENCY_KEY_REUSED.
The account-scoped key and hash live on Appointment, so do not delete booking rows
or recycle keys. Existing replay is returned before validating later configuration
changes; ownership and active account/token checks still apply. Replay responses
retain identical UTC serialization. A future account-wide retention policy is not
yet implemented.

Legacy staff endpoint: GET /api/appointments?skip=0&take=50.
Director/Nurse see only their JWT Clinic. Doctor sees only their own assigned
schedule in that clinic. Other legacy roles get 403. Platform JWTs are rejected
by legacy endpoints and vice versa. Lists have a maximum page size of 100.
Responses contain booking/snapshot fields, not local PatientId, PlatformAccountId,
contact information, password/JWT data or medical notes. Clinical card access is
outside this phase. The customer can list their own bookings across clinics;
clinic staff cannot enumerate that customer's bookings elsewhere.

## Time, snapshots and conflicts

Status is Confirmed initially; Cancelled is reserved in the model for future
explicit cancellation, but no status-changing endpoint is provided. Confirmed rows
block the doctor's [OccupiedStartUtc,OccupiedEndUtc), including both buffers.
Adjacent occupied intervals are permitted. The account also cannot have overlapping
[StartUtc,EndUtc) appointments across clinics; travel time is not modeled.

ClinicSchedulingPolicy runs in the SAME non-deferred SQLite transaction as booking.
Pending timezone fails with 409 CLINIC_TIMEZONE_UNVERIFIED. Clinic/doctor versions,
active/published DoctorProfile, active Doctor User, active Service and DoctorService
must all be valid in the target tenant. Every occupied UTC minute is converted
through the verified IANA timezone and must be covered by DoctorWorkingHours for
that local weekday/minute. GPS, server local timezone and unverified defaults never
participate. PatientAvailability is not a mandatory booking restriction in this
phase: the customer explicitly chooses a time, and matching is not implemented.

Persisted snapshots include UTC start/end and occupied instants, clinic timezone,
clinic/doctor versions, duration/buffers, doctor/service names and price range.
Price range is informational, not a guaranteed final bill. Future configuration
edits do not move appointments or change snapshots. This phase does not yet warn
about later working-hour changes affecting existing bookings; that needs the
future rescheduling workflow. DoctorTimeOff and holidays are not modeled; working
hours must reflect current operational availability until that module exists.

The writer reservation is obtained before reads, preventing two independent
requests/processes against this SQLite database from both seeing a free interval.
Unique (PlatformAccountId,IdempotencyKey) protects duplicate submissions. BEFORE
INSERT and UPDATE SQLite triggers independently reject doctor/account overlaps,
including direct SQL. The trigger is restricted to Confirmed rows. CHECK constraints
bind computed occupied/duration ticks and supported statuses/zones. The composite
Appointment(ClinicId,PatientId,PlatformAccountId) FK references ClinicPatientLink's
same triple, proving BOTH tenant and account ownership at the database boundary.
Doctor and Service tenant FKs are also Restrict. No deletion cascade is used.
These guarantees require a single shared writable database, not independent SQLite
replicas. Booking contention is returned, not automatically retried.

## Migration and validation

20261026141041_AddAppointments creates Appointments, indexes, CHECKs, FKs and two
overlap triggers. It adds an alternate key to ClinicPatientLinks, causing SQLite
to rebuild that table; old columns, row values, named indexes and FKs are retained.
The generated SQL emits warnings because custom trigger creation occurs while the
link rebuild is pending. The SQL was reviewed: triggers reference the already
created Appointments table; they fire only on appointment writes, and the copy
phase writes only ClinicPatientLinks. Both EF and the checked-in SQL are rehearsed
on synthetic in-memory SQLite with an existing link. Triggers are custom migration
SQL: future Appointments rebuilds must explicitly preserve/recreate them.

The link rebuild spans transaction boundaries and temporarily disables FKs. Before
real deployment, review production custom objects, deleted-ID sequence high-water
marks, disk space, backup restore and writer downtime on an authorized copy. Down
removes appointments and triggers; it is not a data recovery mechanism. No old
migration was edited. The historical PlatformMigrationTests still validate their
original migration target; the new migration test checks the current latest state.
Normal non-Testing startup auto-migrates, so do not start the changed API against
production before explicit migration approval.

No real database, backup, API, SMTP/SMS or production identity is used by the tests.
The previous 138 tests remain. New tests cover actual JWT schemes, tenant/ownership,
consent, separate clinic cards, snapshot retention, timezone, buffers/adjacency,
concurrent independent HTTP connections, replay, stale configs, rollback and raw SQL
constraints. Run: dotnet test backend/NexoraDental.sln -c Release
