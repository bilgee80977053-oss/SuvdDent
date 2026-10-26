# Doctor Profile API

All routes require the existing JWT bearer authentication.
Clinic and caller identity come from validated JWT claims. Body/query/header
tenant IDs do not override them. No endpoint creates users or changes roles.

## Routes

| Method | Route | Role |
| --- | --- | --- |
| GET | /api/Doctors | Any authenticated user; own clinic, active published profiles |
| GET | /api/Doctors/{id} | Same catalog rules |
| GET | /api/Doctors/me | Doctor; own profile, including unpublished |
| PUT | /api/Doctors/me | Doctor; own active profile details |
| PUT | /api/Doctors/me/working-hours | Doctor; own active profile schedule |
| GET | /api/admin/Doctors | Director; own clinic's eligible doctor profiles |
| GET | /api/admin/Doctors/{id} | Director; own clinic |
| POST | /api/admin/Doctors | Director; existing active Doctor user in own clinic |
| PUT | /api/admin/Doctors/{id} | Director; details, publication, active status |
| PUT | /api/admin/Doctors/{id}/working-hours | Director; own clinic |

Inactive users and users no longer in the Doctor role are excluded, including
from profile management. Doctor users cannot change ownership, identity,
publication, or active status. Other tenants' IDs return 404, wrong roles 403,
and anonymous requests 401. Duplicate profiles and stale/busy writes return 409.

## Request examples

Create as Director (userId identifies the target; it grants no authority):

    {
      "userId": 20,
      "profile": {
        "specialty": "Orthodontics",
        "practiceStartYear": 2010,
        "biography": "Plain-text introduction",
        "photoPath": null
      },
      "isPublished": false
    }

Doctor details update:

    {
      "version": "<version returned by GET>",
      "profile": {
        "specialty": "Orthodontics",
        "practiceStartYear": 2010,
        "biography": "Plain-text introduction",
        "photoPath": null
      }
    }

Director details update also requires isPublished and isActive.
FullName is read from User.FullName and is not editable here.
Responses use an explicit projection: no password hash, JWT, email, phone,
ClinicId, or UserId is returned.

Working-hours update (replaces the entire weekly schedule):

    {
      "version": "<version returned by GET>",
      "intervals": [
        { "dayOfWeek": 1, "startMinute": 540, "endMinute": 780 },
        { "dayOfWeek": 1, "startMinute": 840, "endMinute": 1080 }
      ]
    }

Sunday is 0 and Saturday is 6. Minutes are local wall-clock time, 0 through
1440, with start < end. Adjacent intervals are allowed; same-day overlaps,
duplicates, null entries, and more than 70 intervals are rejected. An empty
array clears the schedule. Overnight intervals must be split across days.
Weekly hours are not appointment availability; no Appointment table is added.

Profile and schedule updates share one application-managed Guid Version.
Every update must supply its last-read version. A conditional database update
and transaction cover version increment, deletion of old hours, and insertion
of replacements. A stale or SQLite busy/locked request returns 409 with no
automatic retry. Reload before trying again; do not blindly overwrite.
This protects concurrent writes across multiple API instances.
SQL CHECK constraints enforce individual interval bounds; cross-row overlap
validation is performed by these API writes.

PhotoPath may be null or a local /uploads/doctors/{ownClinicId}/filename path
ending in png/jpg/jpeg/webp. Remote URLs and path traversal are rejected.
This change does not implement file upload, image storage, or static-file
hosting. Biography is plain text; clients must render it as text.

## Migration and tests

AddDoctorProfiles creates only DoctorProfiles and DoctorWorkingHours, their
indexes, checks, and restricted foreign keys. Existing tables are not rebuilt.
Its Down drops the new tables and their doctor data; do not use it as backup.
Existing migration files are unchanged.

Do not start a non-Testing API against a real database until migration deployment
is approved: the existing startup still applies pending migrations automatically.
The design-time factory uses in-memory SQLite and does not start the API.

Run:

    dotnet test backend/NexoraDental.sln -c Release

The existing 18 tests remain. Historical tenant-integrity tests explicitly target
their original migration, so new additive indexes do not change that test boundary.
Doctor tests use real JWT middleware, actual migrations, synthetic Clinic A/B,
and a unique shared in-memory database with independent request connections.
A migration test compares the existing five tables' data, columns, FKs, and
indexes before and after applying AddDoctorProfiles.
