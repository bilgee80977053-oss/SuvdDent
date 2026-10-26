# Scheduling foundations (phase 1)

This phase stores clinic timezones, doctor/service durations and patient availability.
It does not create appointments, time off, matching results or notifications.

## Authorization and endpoints

All endpoints use the existing signed JWT middleware. Clinic authority comes only
from its validated clinicId claim; patient ownership is resolved from the database.
Body/query/header clinicId, userId and patientId do not confer authority.
Responses contain scheduling fields only, without personal contact information.

- Director: GET /api/admin/Doctors/{doctorId}/services
- Director: PUT /api/admin/Doctors/{doctorId}/services
- Patient: GET /api/Patients/me/availability
- Patient: PUT /api/Patients/me/availability

Wrong role: 403; missing/invalid JWT: 401. Missing, inactive or other-tenant
resources: 404. Invalid data: 400. Stale version or competing SQLite lock: 409.
Successful GET/PUT: 200. After 409, GET current state and explicitly retry with
its Version, preserving the user's intended edit; no automatic overwrite.

Director PUT example (replace version with GET's value):

```json
{
  "version": "11111111-1111-1111-1111-111111111111",
  "services": [
    { "serviceId": 51, "durationMinutes": 30, "bufferBeforeMinutes": 5,
      "bufferAfterMinutes": 10, "isActive": true }
  ]
}
```

Only active services and active profiles belonging to active Doctor users in the
Director's clinic are eligible. ServiceId selects a target, never grants access.
At most 100 unique service IDs per request. Duration >= 1, buffers >= 0, and total
occupied duration <= 1440 minutes. Omitted associations become inactive rather
than being deleted; their IDs remain stable for future appointment references.
Reactivation updates the existing row. GET includes inactive associations.
The shared DoctorProfile.Version also protects profile and working-hours edits.

Patient PUT example (use GET's Version; the zero UUID is valid only before the
first availability schedule has been created):

```json
{
  "version": "00000000-0000-0000-0000-000000000000",
  "intervals": [
    { "startUtc": "2026-11-01T09:00:00+08:00", "endUtc": "2026-11-01T10:00:00+08:00" },
    { "startUtc": "2026-11-01T02:00:00Z", "endUtc": "2026-11-01T03:00:00Z" }
  ]
}
```

PUT replaces the patient's entire list, at most 100 intervals. Explicit Z or
+/-HH:MM offset, ISO date and seconds are required on both timestamps. Values are
normalized to UTC, persisted as UTC ticks (SQLite INTEGER), and returned with a
zero offset. Start must precede end. Overlaps are compared by absolute instant;
intervals are half-open [start,end), so adjacent intervals are accepted. Date-only,
offsetless, missing and malformed timestamps are rejected. The application does
not infer timezone offsets or resolve ambiguous local timestamps on the caller's
behalf. Cross-midnight windows are allowed. Past windows are currently allowed;
future matching must discard expired windows.

An empty list clears availability but retains PatientAvailabilitySchedule.Version,
preventing old requests from recreating a cleared list. PatientAvailability rows
reference that schedule with a composite FK, and the schedule references Patient
with a composite FK; a direct SQL tenant mismatch fails as well.

## Transactions and concurrency

Writes acquire a SQLite non-deferred transaction before validating resources.
Compare-and-swap Version updates and all child writes commit together. Unique
constraints protect first creation and doctor/service duplicates. Busy/locked
SQLite outcomes become 409; they do not trigger an automatic retry. GETs use a
single read transaction so the returned Version and child list describe the same
snapshot. Foreign keys use Restrict. Duration/individual interval CHECK constraints
also reject invalid raw SQL writes. Overlap validation is enforced by this API's
serialized replacement transaction, not by a database exclusion constraint; future
writers must use the same protocol. No booking concurrency promise is made yet.

## Timezone verification update

The later clinic location migration makes all legacy clinics PendingVerification.
The legacy Asia/Ulaanbaatar value below is a candidate, not a verified timezone.
See ClinicLocationApi.md: future matching/booking/rescheduling must use
ClinicSchedulingPolicy within the operation transaction and fail closed while
Pending. Existing UTC availability editing remains available.

## Timezone and migration strategy

Migration: 20261026120838_AddSchedulingFoundations; review SQL is adjacent in
Migrations/AddSchedulingFoundations.sql. Existing Clinics receive the non-null
IANA default Asia/Ulaanbaatar; the C# initializer uses the same value. This is an
explicit legacy deployment assumption, not timezone detection. Before deployment,
confirm each clinic's actual zone; clinics in another zone need an authorized
correction. No historical appointment timestamps are reinterpreted by this phase.
Use TimeZoneInfo with each clinic's IANA ID for future local working-hour matching;
never use the server's local timezone. There is no timezone editing endpoint yet.

SQLite rebuilds Patients and Services to add (ClinicId,Id) alternate keys. It copies
existing columns, preserves nullable Patient.UserId, the Patient/User FK and named
patient indexes. It adds DoctorServices, PatientAvailabilitySchedules and
PatientAvailabilities, their composite FKs, checks and indexes. No old migration
was edited. Both the EF migration and the checked-in generated SQL are tested
against a prior schema with synthetic data, including existing doctor hours.

Rebuilds require write downtime and space for table copies. SQLite emits multiple
transaction boundaries and temporarily disables foreign keys during table swaps;
a crash can require restoring an independently verified backup. Custom triggers
or indexes outside the EF model need a separate production schema review. Rebuilds
can reset AUTOINCREMENT high-water marks to the maximum retained ID if higher rows
were previously deleted; live IDs and subsequent generation are tested, but no
claim is made about deleted IDs. Down migration removes scheduling data.

The real database and backups have not been inspected or modified for this work.
Before deployment, verify a backup and rehearse on an authorized isolated copy,
checking schema, counts, data, FKs, integrity and migration history. Normal API
startup auto-applies migrations outside Testing: do not run the changed API against
a real database before approving deployment. Integration tests use Testing,
synthetic data, isolated in-memory SQLite and the actual JWT middleware; startup
seeding is skipped and no real SMTP service is configured.

Run: dotnet test backend/NexoraDental.sln -c Release
