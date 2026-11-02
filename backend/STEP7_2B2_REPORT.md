# STEP 7.2B-2 — Notification Preferences

Date: 2026-11-03 (Asia/Ulaanbaatar)
Branch: feature/school-appointment-mvp
Base commit: ba20a3f

## Inspection

Verified the requested branch and base commit with a clean working tree before editing.

Reviewed NotificationPreference, its composite tenant/user key and foreign key, notification queue/processor consent and verification checks, AppointmentsController, JWT identity validation, Auth/me, and LIVE profile/controller/navigation. The existing model already supports the required preferences; no schema migration is necessary.

## Backend implementation

New authenticated Patient-only endpoints:

- GET /api/notification-preferences/me
- PUT /api/notification-preferences/me

Both derive clinicId and userId exclusively from validated JWT claims. Extra tenant/user/verification values in body or query cannot override identity or contact ownership.

GET returns 200 with the two consent flags and read-only contact-verification metadata. Missing preferences return both flags as false without inserting a record.

PUT requires both boolean consent fields and atomically inserts or updates the existing NotificationPreference record. Missing, null or invalid consent fields return 400 rather than accidentally replacing stored consent with defaults. Unauthenticated callers receive 401; non-Patient callers receive 403.

Request:

~~~json
{"emailEnabled":true,"smsEnabled":false}
~~~

Response shape for GET and PUT:

~~~json
{"emailEnabled":true,"smsEnabled":false,"isEmailVerified":true,"isPhoneVerified":false}
~~~

The verification metadata describes verified, nonempty User contacts. It is read-only and separate from consent. No endpoint writes verification flags or changes contact information.

The parameterized INSERT ... ON CONFLICT statement is supported by both configured database providers. It updates both flags together and avoids a read-then-insert race. Concurrent successful writes use last committed write wins, with one row per clinic/user. A PUT response reads the stored current settings, which may reflect another concurrent writer.

NotificationQueue and NotificationProcessor are unchanged. They continue to require opt-in, active user/patient eligibility, verified contact information, matching recipients and appropriate appointment status. Once withdrawal is committed, future processing reads the withdrawn consent and suppresses queued notifications, including scheduled reminders.

Existing endpoints, DTOs, appointment behavior, migrations, model snapshots and delivery configuration are unchanged. Only new preference DTOs and endpoints were added.

## LIVE Flutter implementation

- Added a dedicated Mongolian notification settings view linked from the existing LIVE profile and authenticated toolbar.
- Email and SMS switches are explicitly labelled as notification consent.
- Loads persisted settings on entry; supports explicit refresh and save.
- Switches and save remain disabled until settings load successfully and while an operation is pending.
- Shows loading, save acknowledgement and safe error messages.
- Unknown save outcomes do not claim success or automatically resend. The user is told to refresh settings from the backend.
- Refresh replaces local drafts with backend state.
- Unverified phone metadata produces a clear message that SMS will not be sent and that phone verification is not currently available in the app.
- General explanatory text states that consent does not verify contact ownership or activate a delivery service.
- No fake verification or SMS activation indicator was added.
- Controller preferences and drafts are cleared on logout/clinic change. Existing session-generation guards discard stale loads and save responses.
- The settings view stops showing consent controls when its original session changes.
- Back navigation returns to the existing booking/profile stage without changing booking selections or cancellation behavior.

## Changed files

Backend:

- DentalApi/Contracts/NotificationPreferenceContracts.cs (new)
- DentalApi/Controllers/NotificationPreferencesController.cs (new)
- DentalApi/Services/NotificationPreferenceService.cs (new)
- DentalApi/Program.cs (service registration)
- DentalApi.IntegrationTests/NotificationPreferenceApiTests.cs (new)
- DentalApi.IntegrationTests/AppointmentApiTests.cs (HTTP consent/outbox checks)

Flutter:

- mobile/lib/live/models.dart (new preference response model)
- mobile/lib/live/dental_api.dart (GET/PUT wrappers)
- mobile/lib/live/live_flow_controller.dart (drafts, loading/saving, session cleanup)
- mobile/lib/live/live_patient_flow.dart (profile/toolbar navigation)
- mobile/lib/live/notification_settings_screen.dart (new)
- mobile/test/notification_preferences_test.dart (new)

Report:

- backend/STEP7_2B2_REPORT.md (new)

Backend paths above are relative to backend; mobile paths are relative to the repository root.

## Validation

| Command | Final result |
|---|---|
| dotnet build backend\NexoraDental.sln | PASS: 0 warnings, 0 errors |
| dotnet test backend\NexoraDental.sln | PASS: 400 passed, 0 failed, 12 skipped; 412 total |
| flutter analyze (from mobile) | PASS: no issues found |
| flutter test (from mobile) | PASS: 89 passed, 0 failed, 0 skipped |
| git diff --check | PASS: no whitespace errors |

The 12 skipped backend cases are existing live PostgreSQL workflow/concurrency tests; NEXORA_PG_TEST_CONNECTION is absent.

15 new backend cases cover default flags, read-only default behavior, create/update/withdrawal, separate verification state, invalid requests, unauthenticated/non-Patient callers, spoofed identities, same-clinic user and cross-clinic isolation, concurrent initial/existing preference writes, queued notification suppression following HTTP withdrawal, and unverified-phone queueing rules.

9 new Flutter tests cover loading, exact consent-only request bodies, switches, save acknowledgement, refresh, failed loading, uncertain save reconciliation, stale load/save responses after logout/session changes, 401 cleanup, loading-disabled controls, verification explanation, profile/toolbar navigation and preserved booking stage.

During validation, JSON string quoting in new C# test data and Dart style findings were corrected. The initial full Flutter run had one navigation test failure because it searched for an OutlinedButton while the existing profile helper renders a FilledButton. The corrected focused and full suites pass.

## Limitations and safety

- Withdrawal applies to future notification processing after the write commits. It cannot recall messages already accepted by an external provider or undo an in-flight send that has already passed its consent check.
- Concurrent writes use last committed write wins; no new ETag/version-conflict protocol was introduced.
- Live PostgreSQL preference upserts/concurrency were not executed in this environment.
- Phone-verification functionality remains unavailable in the LIVE app. Consent may be stored independently, but unverified contacts remain ineligible for delivery.
- Enabling consent does not backfill historical notifications or revive records already suppressed by the existing processor.
- Network failures require connectivity and explicit refresh to resolve an uncertain save.
- No manual device/browser session against a running backend was performed; HTTP integration and mocked Flutter tests provide the validation.
- No schema changes or database migration commands were run. Existing tests create/migrate only disposable SQLite in-memory fixtures.
- No real Email/SMS delivery was enabled or exercised. No real credentials, production databases or production data were used.
- Existing notification consent/verification checks remain intact.
- No edits to main, branch switches, commits or pushes.

## Final git status --short
~~~text
 M backend/DentalApi.IntegrationTests/AppointmentApiTests.cs
 M backend/DentalApi/Program.cs
 M mobile/lib/live/dental_api.dart
 M mobile/lib/live/live_flow_controller.dart
 M mobile/lib/live/live_patient_flow.dart
 M mobile/lib/live/models.dart
?? backend/DentalApi.IntegrationTests/NotificationPreferenceApiTests.cs
?? backend/DentalApi/Contracts/NotificationPreferenceContracts.cs
?? backend/DentalApi/Controllers/NotificationPreferencesController.cs
?? backend/DentalApi/Services/NotificationPreferenceService.cs
?? backend/STEP7_2B2_REPORT.md
?? mobile/lib/live/notification_settings_screen.dart
?? mobile/test/notification_preferences_test.dart
~~~
