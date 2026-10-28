# STEP 4 review report — Nexora Dental

Date: 28 October 2026 (Asia/Ulaanbaatar).
Branch: `feature/school-appointment-mvp`.
Preflight: correct branch, clean working tree, Step 3 endpoints present, actual
controllers/contracts/CORS/documentation inspected, all five original Flutter
tests passed before editing. No AGENTS.md files were found in the repository.

## 1. Implemented LIVE screens

The existing Material theme and eight-step journey remain recognizable:
configured test clinic, login/registration, email code verification, own patient
profile load/create, server services, service-filtered doctors, clinic-calendar
date and server slots, booking review/confirmation. An authenticated app-bar
action opens paginated My appointments. Forms are scrollable, controls expose
labels/tooltips, selected tiles show selection, async errors use live regions,
and buttons disable while requests are pending. A narrow-screen login widget
test and full mocked registration/booking journeys passed.

LIVE requires `NEXORA_API_BASE_URL` and positive `NEXORA_CLINIC_ID`. Invalid LIVE
settings show a configuration error. No suitable safe public clinic discovery
endpoint exists, so LIVE never treats demo clinic IDs as database IDs. The UI
labels the only configured clinic as a test clinic. Setup and safe ID retrieval
are in [README.md](README.md), including web, emulator and physical Android.

## 2. Integrated existing endpoints

| Method | Path |
| --- | --- |
| POST | `/api/Auth/login` |
| POST | `/api/Auth/register` |
| POST | `/api/Auth/verify` |
| GET | `/api/Auth/me` |
| GET | `/api/Patients/me` |
| POST | `/api/Patients/me` |
| GET | `/api/appointments/services` |
| GET | `/api/appointments/doctors?serviceId=...` |
| GET | `/api/appointments/slots?doctorProfileId=...&serviceId=...&date=yyyy-MM-dd` |
| POST | `/api/appointments` |
| GET | `/api/appointments/me?page=...&pageSize=20` |

The client uses the exact existing JSON contracts. In particular, verification
returns a receipt rather than a token; profile creation returns fewer fields
than profile GET; `/Auth/me` returns nullable string claims and no verified-email
flag. Booking sends only doctorProfileId, serviceId, startUtc and requestId.
Server-returned IDs, times, timezone and status drive confirmation and history.

## 3. Exact files changed

Modified tracked files:

| File | Purpose |
| --- | --- |
| `.gitignore` | Allow the debug source manifest through the existing broad Debug-directory ignore |
| `mobile/README.md` | Current mode, contract, session, error, networking and validation guide |
| `mobile/android/app/src/main/AndroidManifest.xml` | INTERNET permission for API access; no release cleartext exception |
| `mobile/lib/app/nexora_app.dart` | Explicit DEMO/LIVE/configuration-error routing using the shared theme |
| `mobile/pubspec.yaml` | http/timezone dependencies and updated application description |
| `mobile/pubspec.lock` | Resolved dependency versions |

Created/new-to-Git files:

| File | Purpose |
| --- | --- |
| `mobile/android/app/src/debug/AndroidManifest.xml` | Debug-only HTTP allowance, based on existing ignored generated manifest |
| `mobile/lib/live/models.dart` | Configuration and typed API DTOs |
| `mobile/lib/live/dental_api.dart` | Injectable cross-platform HTTP client, strict response statuses, timeouts and safe errors |
| `mobile/lib/live/live_flow_controller.dart` | Memory session, resets, timezone formatting, UUID booking intents, retry/concurrency guards and pagination |
| `mobile/lib/live/live_patient_flow.dart` | Responsive LIVE screens and configuration errors |
| `mobile/test/live_api_test.dart` | 39 mock HTTP/widget tests |
| `mobile/STEP4_REPORT.md` | This report |

No backend files, migrations, historical Flutter tests, demo controller, fixtures,
demo feature screens or shared theme changed.

## 4. DEMO preservation

No API base URL selects the original offline demonstration, including both
synthetic clinics, fixed sample slots and explicit no-booking/no-email completion.
It creates no HTTP client/session and stores no token. All five original tests
still pass. Failed LIVE requests never substitute synthetic records.

## 5. Authentication and booking behavior

Real server JWTs are retained only in memory and sent with Bearer authorization.
There is no credential/token persistence or secret logging. Password/code form
values clear after submission. Restart requires login again. Logout, clinic/API
configuration change, a new login and revoked HTTP 401 clear prior patient state;
late responses cannot restore old identity or data. Clinic switching is covered
at both controller and widget reconfiguration boundaries.

Registration advances only after a successful backend response. Verification
requires a real submitted code and successful backend response, then returns to
login. SMTP and verification errors remain errors. The client never manufactures
a JWT or verified user; booking eligibility remains server-authoritative.

Only server slots are selectable. Returned IANA timezone IDs drive labels with
UTC offsets, while UTC instants remain unchanged in booking requests. Services,
doctors and dates clear dependent selections. UUID v4 request IDs remain stable
across timeouts and retries of the same intent. Busy guards block double taps.
Only parsed HTTP 201/200 appointment responses show booking success. A 409 clears
the invalid slot and offers an explicit refresh; even a failed refresh leaves the
slot screen available for another retry. Unknown booking outcomes lock edits and
preserve the intent until retried. No authoritative ClinicId, PatientId or duration
is sent for booking.

## 6. Flutter validation

| Command/check | Actual result |
| --- | --- |
| `flutter pub get` from mobile | Passed; lockfile updated |
| `flutter analyze` | Passed, no issues |
| `flutter test` | 44 passed: 5 original + 39 new; no failures |
| LIVE `flutter build web` using HTTPS `.invalid` test origin and synthetic clinic configuration | Passed; no server contacted |
| Web Wasm dry run | Succeeded |

Coverage includes configuration/demo isolation, login success/failure and bearer
headers, wrong clinic/role, revoked authentication, clinic switching, registration
and verification/SMTP errors, profile GET/POST, services/doctor filtering, slot
query/empty/unknown timezone cases, dependent resets, UTC/Hovd display, booking
201/200, 409 and failed-refresh recovery, timeout and delayed responses, stable
UUIDs, double taps, late-session protection, pagination, malformed/network/status
errors, secret-free output and LIVE widget journeys.

The web build emits a font-family notice mentioning CupertinoIcons while only
MaterialIcons is bundled. The app uses Material icons, and compilation succeeds;
this is recorded rather than claimed as a visual/device verification. Pub also
reports four newer versions outside the existing dependency constraints; unrelated
baseline dependency upgrades were not performed.

## 7. Backend validation

`NEXORA_PG_TEST_CONNECTION` was confirmed absent without printing connection
values. `dotnet build .\backend\NexoraDental.sln` passed with 0 warnings and
0 errors. `dotnet test .\backend\NexoraDental.sln` passed: 344 passed, 12 skipped,
0 failed (356 total). The skipped tests require explicitly opted-in PostgreSQL.
No real PostgreSQL connection, backend server startup, production access or
automatic database migration was performed. Existing in-memory integration tests
retain their own isolated test database behavior.

## 8. Manual testing

No manual live end-to-end, browser visual or native Android device test was
performed. No development backend/database, SMTP session or verified user was
used. Widget tests use synthetic mock HTTP responses. A successful web build does
not prove live network, CORS, email delivery or Android runtime behavior.

## 9. Remaining runtime requirements and risks

- The development owner must provide the actual clinic ID and approved API
  listener; no real ID or production URL was invented or configured.
- Browser development requires exactly `http://localhost:7357` in the existing
  allowed-origins list when following README commands. No wildcard CORS change.
- Real registration needs working SMTP; the server may retain an inactive account
  after delivery failure. Existing resend/recovery/admin workflow is needed in
  that case; no new recovery UI or email infrastructure is added here.
- A verified-email Patient account, own profile, verified clinic timezone and
  active doctor/service/schedule configuration are needed for real booking.
- In-memory tokens and unresolved intents disappear on restart/logout. Check My
  appointments after logging back in if a prior booking response was lost.
- The initial proposed date is the current device calendar date, interpreted as
  a clinic-local date; the backend validates the true local booking window.
- Unknown timezone IDs fail closed for selecting slots; history labels UTC with
  a clear timezone error instead of guessing a clinic offset.
- PostgreSQL startup protection, database migration policy and production security
  remain unchanged. Native release/device validation remains outstanding.

## 10. Git state and stopping point

Final changes are unstaged: 6 modified tracked files and 7 new/untracked files
listed above. `git diff --check` passes. `git status --short` and `git diff --stat`
were inspected; normal diff stat covers tracked modifications only and excludes
new files until staged. `git diff --name-only -- backend` is empty.

No commit, push, branch change or STEP 5 work was performed. Stopped after STEP 4
for review.
