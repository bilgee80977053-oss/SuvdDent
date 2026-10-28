# Nexora Dental mobile — university MVP, STEP 4

Student: В242270028 — А. Билэгсайхан

Assignment: Nexora Dental — Шүдний эмнэлгийн цаг захиалгын аппликейшн

## Two explicit modes

Without `NEXORA_API_BASE_URL`, the original **DEMO** walkthrough runs unchanged.
It uses synthetic clinics 101/202 and frozen example slots, makes no HTTP requests,
asks for no real credentials, sends no email and creates no booking or token.
Its original eight steps, Material theme and five regression tests are preserved.

With `NEXORA_API_BASE_URL`, **LIVE** runs the same eight-stage journey using the
existing backend. Invalid configuration shows an error; it never falls back to
synthetic data. No production server has been configured or contacted by this work.

The API base URL must be an HTTP(S) server root (no `/api` suffix, credentials,
query or fragment). Release mode requires HTTPS. No secrets belong in dart-defines.

## Development configuration

There is no suitable public clinic discovery endpoint. `/api/locations` lists
administrative regions and requires authentication; it is not a clinic directory.
The public service list is not a clinic directory either. LIVE therefore requires
`NEXORA_CLINIC_ID`, a positive integer for an explicitly configured test clinic.
The UI identifies this as a configured test clinic. Demo IDs must never be reused
as database IDs by assumption.

Obtain the ID from the development environment owner/director, who can inspect
the authorized development clinic record or its existing authenticated clinic
identity. Do not probe public IDs, use production records or expose a new public
clinic API. Only one configured LIVE clinic is offered. Reconfiguring the clinic
requires restarting; the controller also clears all state on a clinic change.

Run commands below from `mobile/`. In PowerShell set `$clinicId` to the ID supplied
by the development environment owner; it is intentionally not supplied here.
Use an already prepared, authorized development backend. Do not start the API
against an unknown database: existing SQLite startup may migrate automatically,
and PostgreSQL startup protection remains enabled. This step changes neither.

### Flutter web on localhost

Use a stable browser origin:

```powershell
flutter pub get
flutter run -d chrome --web-hostname localhost --web-port 7357 --dart-define=NEXORA_API_BASE_URL=http://localhost:5080 --dart-define=NEXORA_CLINIC_ID=$clinicId
```

`5080` is an example development API port; use the actual approved listener.
Include exactly **`http://localhost:7357`** (no trailing slash/path) in the backend's
existing `Cors:AllowedOrigins` list, for example `Cors__AllowedOrigins__0` if that
entry is free; preserve other allowed entries. The API already uses the named
`ConfiguredOrigins` policy. No backend CORS code was changed and no wildcard is
needed. A different web port/hostname requires its exact origin to be allowed.
HTTPS web pages require a trusted HTTPS API to avoid mixed-content blocking.
Do not disable certificate validation. Browser CORS failures appear as connection
errors and should be diagnosed using the local browser network tools.

### Android emulator

```powershell
flutter run -d <emulator-id> --dart-define=NEXORA_API_BASE_URL=http://10.0.2.2:5080 --dart-define=NEXORA_CLINIC_ID=$clinicId
```

`10.0.2.2` is the standard Android emulator address for the host. Android localhost
refers to the emulator itself. The debug source manifest enables HTTP only for
debug builds. Main/release gains INTERNET permission but no cleartext exception.
Use a trusted HTTPS endpoint for release. Android HTTP clients do not use browser
CORS, but the API must still be reachable from the emulator.

### Physical Android device

Use the development computer's private LAN address, the correct port, and a
backend listener/firewall scoped to the trusted development network:

```powershell
flutter run -d <device-id> --dart-define=NEXORA_API_BASE_URL=http://<development-LAN-IP>:5080 --dart-define=NEXORA_CLINIC_ID=$clinicId
```

Both devices must reach the same trusted network. Do not expose the API publicly.
Debug HTTP is for isolated synthetic development accounts only; prefer trusted
HTTPS. Do not copy real patient data or credentials into tests or source files.
Native Android/iOS builds still require their installed platform toolchains.

## LIVE journey and API contracts

| Stage | Existing endpoints and behavior |
| --- | --- |
| 1. Clinic | Explicit configured development clinic; no discovery request |
| 2. Authentication | `POST /api/Auth/login` with `identifier`, `password`, `clinicId`; or `POST /api/Auth/register` with `fullName`, `phone`, `email`, `password`, `clinicId` |
| 3. Verification | Registration returns `message`, `userId`; `POST /api/Auth/verify` sends `userId`, `code`, then requires login. Successful login also calls `GET /api/Auth/me` |
| 4. Patient | `GET /api/Patients/me`; only 404 opens creation. `POST /api/Patients/me` sends `fullName`, `phone`, optional/null `dateOfBirth`, `address`; required name/phone forms validated |
| 5. Service | `GET /api/appointments/services`, using only server catalog entries |
| 6. Doctor | `GET /api/appointments/doctors?serviceId=...` |
| 7. Slot | `GET /api/appointments/slots?doctorProfileId=...&serviceId=...&date=yyyy-MM-dd` |
| 8. Confirm | `POST /api/appointments` with exactly `doctorProfileId`, `serviceId`, `startUtc`, `requestId`; only 201/200 shows success |
| My appointments | `GET /api/appointments/me?page=...&pageSize=20`; previous/next page controls use server page metadata and item count; no invented total |

Profile GET has `id`, `fullName`, `phone`, nullable `dateOfBirth`, `address`.
Profile POST 201 returns only `id`, `fullName`, `phone`; DTOs account for this.
Auth login returns `token`, `userId`, `fullName`, `role`, `clinicId`. `/Auth/me`
returns nullable string claim fields `userId`, `name`, `role`, `clinicId`; it does
**not** expose email verification status. The app never infers a verified flag
from login or a button click; booking eligibility is checked by the server.
See [backend booking contracts](../backend/DentalApi/AppointmentApi.md) for the
complete catalog, slots, appointment response and idempotency contracts.

Registration requires working real email delivery. SMTP 503 and verification
errors are shown without bypasses. Verification success does not supply a JWT:
the app returns to login. Expired codes require the existing backend recovery
workflow/admin assistance; resend/password recovery screens are outside this MVP.
The backend can retain an inactive registration after SMTP failure, so repeating
registration may produce a conflict. Do not bypass verification to recover it.

## Session, time and retry behavior

JWTs live only in the API client's memory and are sent as Bearer tokens. Passwords
and codes are transient form values and cleared after submission. There is no
localStorage, preferences, token persistence or logging of secrets. App restart
requires login again. Logout, clinic change and authenticated HTTP 401 clear
identity, profile, catalogs, slots, booking intent and appointment history.
Late responses cannot restore a previous session. A new login clears previous
patient state before authenticating. Authorization always remains server-side.

Service changes clear doctor, availability and booking intent; doctor/date changes
clear availability and booking intent. Slots come only from the server. The date
picker is explicitly a clinic calendar date, not an instant; the initial proposed
date uses the current device calendar (never the frozen demo date). The server
validates the actual clinic-local today/30-day window. Returned `timeZoneId`
controls displayed local times through the bundled IANA database. Supported
backend locations currently use `Asia/Ulaanbaatar` or `Asia/Hovd`. Unknown zones
fail closed for slot selection; appointment history can show explicitly labeled
UTC with an error rather than guessing. Labels include offsets to distinguish
ambiguous clock times. UTC instants are preserved in booking requests.

Each newly selected slot starts a UUID v4 booking intent. Double taps are blocked.
Timeout/network/5xx/invalid successful response leave the result uncertain and
retain the same request ID and selections. The UI locks edits until an explicit
retry resolves that intent, so it cannot accidentally create a second intent
because a response was lost. Retrying an existing intent is allowed even if its
slot has since expired: the backend may return the already committed booking.
On 409 the invalid slot/intent is cleared, the doctor/service remain, and an
explicit refresh requests fresh server availability. There are no automatic
POST retries. Success details come exclusively from the returned appointment.

Logout/app restart deliberately loses in-memory intents too. If a response was
lost before leaving, log in and check My appointments before making a new booking.
No persistent booking-intent recovery is claimed by this MVP.

Clear Mongolian error states cover network/CORS failures, timeout, malformed
responses, 400, 401, 403, 404, 409, 429, 503 and other server errors. Known short
Mongolian domain errors can be shown; HTML, stack traces and arbitrary diagnostic
payloads are replaced with safe status messages. Empty data stays empty.

## Implementation and validation

- `lib/app/`, `lib/features/`, `lib/data/`, `lib/core/`: preserved DEMO and shared theme.
- `lib/live/models.dart`: configuration and typed response DTOs.
- `lib/live/dental_api.dart`: injectable cross-platform `http.Client`, strict statuses,
  bounded requests, safe errors and memory-only authorization.
- `lib/live/live_flow_controller.dart`: state transitions, selection resets, timezone
  formatting, UUID intents, request/session generation guards and pagination.
- `lib/live/live_patient_flow.dart`: responsive LIVE forms and eight-stage shell.
- `test/live_api_test.dart`: mock HTTP and LIVE widget tests; no database or SMTP.

Runtime dependencies are [http](https://pub.dev/packages/http) and
[timezone](https://pub.dev/packages/timezone), with versions in `pubspec.lock`.
No retry middleware, insecure storage, TLS overrides or backend features added.

```powershell
flutter pub get
flutter analyze
flutter test
# Optional compilation check; does not contact a server:
flutter build web --dart-define=NEXORA_API_BASE_URL=https://development.invalid --dart-define=NEXORA_CLINIC_ID=7
```

From the repository root, only with the PostgreSQL test opt-in absent:

```powershell
if (Test-Path Env:NEXORA_PG_TEST_CONNECTION) { throw 'Remove the PostgreSQL test opt-in before offline validation.' }
dotnet build .\backend\NexoraDental.sln
dotnet test .\backend\NexoraDental.sln
git diff --check
git status --short
git diff --stat
```

No real backend/database/verified user/SMTP session was used for manual end-to-end
validation. Mock tests and web compilation do not establish live end-to-end or
native Android success. Backend source, migrations, PostgreSQL startup protection,
CORS policy and email infrastructure remain unchanged. STEP 5, commits and pushes
require review and are not included here.
