# NEXORA clinic location and timezone verification

NEXORA supports clinics across all 21 Mongolian aimags and Ulaanbaatar. A legacy
Asia/Ulaanbaatar value is a candidate, never proof that scheduling is safe.

## Offline location catalog

ReferenceData/MongoliaLocations.json is embedded, with string NSO-style codes from
FHIR MN Core 1.0.0: https://fhir.mn/CodeSystem-provinces-cities.json and
https://fhir.mn/CodeSystem-districts-soums.json (retrieved 2026-10-26).
Only two-digit province/capital and four-digit soum/district entries are included;
bag/khoroo entries in the source are deliberately excluded. The snapshot contains
22 province/capital entries and 339 soum/district entries (330 soums, 9 districts).
Country is MN. District codes must belong to the supplied province code.

Suggestions use Asia/Hovd for codes 83 (Bayan-Olgii), 84 (Hovd), 85 (Uvs), and
Asia/Ulaanbaatar elsewhere, following https://data.iana.org/time-zones/tzdb/zone1970.tab.
The mapping is a suggestion only. Source updates require review and tests, not
runtime network requests or automatic changes to verified clinics. Both IANA IDs
must be resolvable by the deployment OS timezone database.

## API

Authenticated users:
- GET /api/locations: MN and the province/capital catalog.
- GET /api/locations/{province}/districts: children, or 404 for an unknown province.
- POST /api/locations/timezone-suggestion: LocationSelection; does not write.

Director only, scope always from the authenticated JWT and current database user:
- GET /api/Clinic/me/location
- PUT /api/Clinic/me/location
- POST /api/Clinic/me/timezone-confirmation

PUT example (replace Version with the GET response):

```json
{
  "version": "11111111-1111-1111-1111-111111111111",
  "location": {"countryCode":"MN", "provinceOrCapitalCode":"11", "soumOrDistrictCode":"1101"},
  "addressLine": "Synthetic address"
}
```

Confirmation example:

```json
{"version":"11111111-1111-1111-1111-111111111111", "timeZoneId":"Asia/Ulaanbaatar"}
```

Confirmation requires a complete valid location and a timezone matching its
suggestion; unsupported or mismatched zones return 400. Exceptions to the location
mapping need separate operator review; this version cannot silently override it.
Status is serialized as the enum value: PendingVerification=0, Verified=1.
The response separates CandidateTimeZoneId, SuggestedTimeZoneId and
EffectiveTimeZoneId; Pending always has a null effective timezone.
No personal user names, email, phone, JWT or password data are returned.
Only the owning Director receives the clinic address; the shared catalog contains
public administrative locations only.

Location/address changes invalidate confirmation and clear verifier metadata, even
within the same timezone. Saving an identical normalized address/location preserves
verification but rotates Version. Each successful write rotates Version. Missing
or stale versions cannot overwrite a newer edit; a legacy clinic's initial zero
UUID is valid until its first successful change. New Clinic objects get new UUIDs.
JWT/body/query/header tenant spoofing cannot select another clinic. Unauthorized
requests return 401, wrong roles 403, invalid input 400, competing/stale writes 409.
Get the current state after a conflict and explicitly resolve the edit.

## Scheduling policy integration contract

ClinicSchedulingPolicy.RequireVerifiedAsync must be called inside the SAME
non-deferred SQLite transaction as future suggestion generation, appointment
creation or rescheduling. It rejects missing/unverified/incomplete/unresolvable
configuration and inactive/non-Director verifiers with Status=409 and
ErrorCode=CLINIC_TIMEZONE_UNVERIFIED. Callers must serialize that error code, return
before producing slots or writing bookings, and never fall back to the legacy
candidate or server local timezone. Expected Clinic.Version can also be checked;
a stale proposal returns CLINIC_SCHEDULING_VERSION_CONFLICT.

This change does not create Appointment, DoctorTimeOff, matching or Notification
endpoints. Therefore no existing booking endpoint is claimed to be protected.
Existing doctor-service editing, working-hour editing and explicit-UTC patient
availability remain usable while Pending. They do not generate/book appointment
slots. PatientAvailability remains absolute UTC and is never shifted on clinic
location changes. DoctorWorkingHours remains clinic local wall time; future slot
conversion must use the verified zone, a concrete date, and explicit handling of
invalid/ambiguous local times. Future bookings must retain UTC instants and a
booking-time timezone/duration snapshot; no automatic rescheduling is authorized.

## Schema and rollout

A new migration follows AddSchedulingFoundations; old migrations are unchanged.
Every existing clinic defaults to PendingVerification, with nullable location and
verification metadata. No geography is inferred from private legacy data.
TimeZoneVerifiedClinicId is an additional redundant tenant key. The CHECK requires
it to equal Clinic.Id for Verified rows, and a composite Restrict FK binds it and
TimeZoneVerifiedByUserId to User(ClinicId,Id). This avoids making Clinic.Id itself
a foreign-key property, preserving SQLite AUTOINCREMENT behavior.
Verified rows must have supported timezone, MN, location fields, nonblank address,
verifier and timestamp. Application validation checks actual catalog membership
and active Director role. Pending rows must have all verifier metadata null.
Clinic.Version is a concurrency token; legacy rows receive a zero UUID, with
compare-and-swap still scoped to the individual clinic. It is not a credential.

SQLite rebuilds Clinics for the new FK/CHECK. The generated SQL copies all old
columns/IDs and preserves AUTOINCREMENT; it temporarily disables FK enforcement
for the table swap and spans transaction boundaries. Production custom triggers,
indexes and deleted-ID sequence high-water marks require separate review before
rollout. Backups, space and writer downtime are required. Down loses location and
verification metadata. Neither real dental.db nor any backup was inspected or
modified. Do not start the non-Testing API against production: startup still
applies pending migrations. Deploy guards and both migrations together during
approved downtime, rehearsing first on an authorized isolated copy.

Integration tests use only synthetic SQLite in-memory databases and the actual JWT
middleware. Sequential migration tests start at AddDoctorProfiles, apply
AddSchedulingFoundations, then the new verification migration through both EF and
generated SQL, checking row values, old schemas/indexes, integrity, FKs, pending
status and ID generation. Historical scheduling tests still validate their original
migration boundary, including the candidate default, without treating it as verified.
