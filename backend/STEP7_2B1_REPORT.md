# STEP 7.2B-1 — Flutter Appointment Cancellation

Date: 2026-11-03 (Asia/Ulaanbaatar)
Branch: feature/school-appointment-mvp
Base commit: 5480211

## Implemented behavior

### Backend

Added POST /api/appointments/{id}/cancel to AppointmentsController.

- Inherits the existing authenticated Patient-role authorization.
- Derives ClinicId and UserId from validated JWT claims. Body/query identity values cannot override them.
- Delegates directly to the existing AppointmentService.Cancel; its implementation and notification consent rules are unchanged.
- Returns the existing AppointmentResponse on success, including Status=Cancelled.
- Preserves service results: 400 invalid ID; 404 missing/foreign appointment or patient; 409 started/past appointment or transaction conflict; 200 success or repeated cancellation.
- Cancellation and eligible notification outbox records retain their existing transactional behavior. Repeated cancellation does not add outbox records.
- Existing routes, DTOs, migrations, model snapshots, and database schema are unchanged.

### Flutter LIVE history

- Confirmed appointments whose start is in the future offer a Mongolian cancellation action. Past/started/cancelled appointments do not.
- A confirmation dialog shows appointment time and offers Буцах / Тийм, цуцлах. No request is sent if declined.
- The controller rechecks eligibility when confirmed, so an appointment that starts while the dialog is open cannot be submitted by the client. The server remains authoritative for timing.
- Existing loading state disables duplicate actions. After confirmation, the list scrolls to its loading/result feedback so it is visible even when the selected card was near the bottom.
- Successful cancellation updates the matching local appointment and refreshes the current history page with its existing page size.
- Mongolian success/error messages distinguish a successful cancellation from a failed history refresh. A refresh failure does not revert known cancellation success.
- Existing booking selections and request IDs are preserved when cancelling a history appointment.
- Session/clinic generation guards discard stale responses. Confirmation is ignored if the session or API changed while the dialog was open.

### Safe timeout and connection-failure reconciliation

- Added a Flutter wrapper for the existing GET /api/appointments/{id}, plus the new POST cancellation wrapper.
- Timeout, lost connection, malformed success response, and server errors do not cause an automatic repeat POST.
- The controller reconciles using the authenticated single-appointment GET.
- If GET reports Cancelled, the controller confirms success and refreshes history.
- If GET reports Confirmed, cancellation remains uncertain: the original request could still be in flight. No failure or success is falsely asserted.
- If reconciliation fails, uncertainty remains visible with a Төлөв шалгах action.
- The user may explicitly retry cancellation after another confirmation dialog. This targets the same appointment ID and relies on existing server idempotency.
- Uncertainty remains checkable even if the appointment has since started; cancellation retries are hidden for past appointments.
- Definitive 400/404/409 errors are displayed without claiming success. Existing uncertain state is not discarded merely because a later attempt returns an error.
- Cancellation and reconciliation validate the returned appointment ID; cancellation also requires the existing Cancelled status.

## Changed files

- backend/DentalApi/Controllers/AppointmentsController.cs — additive cancellation route.
- backend/DentalApi.IntegrationTests/AppointmentApiTests.cs — 10 new HTTP integration cases.
- mobile/lib/live/dental_api.dart — cancellation and single-appointment API calls.
- mobile/lib/live/live_flow_controller.dart — cancellation state, guards, history refresh, reconciliation.
- mobile/lib/live/live_patient_flow.dart — history action, confirmation dialog, visible feedback and check/retry actions.
- mobile/test/cancellation_test.dart — 15 new API/controller/widget tests using existing synthetic fixtures.
- backend/STEP7_2B1_REPORT.md — this report.

## Validation results

| Command | Final result |
|---|---|
| dotnet build backend\NexoraDental.sln | PASS: 0 warnings, 0 errors |
| dotnet test backend\NexoraDental.sln | PASS: 385 passed, 0 failed, 12 skipped; 397 total |
| flutter analyze (from mobile) | PASS: no issues found |
| flutter test (from mobile) | PASS: 80 passed, 0 failed, 0 skipped |
| git diff --check | PASS: no whitespace errors |

The 12 backend skips are existing live PostgreSQL workflow/concurrency tests. NEXORA_PG_TEST_CONNECTION is absent.

HTTP coverage includes unauthenticated and non-Patient callers, same-clinic foreign patients, foreign clinics, spoofed body/query identities, response DTO equality, repeated cancellation, readback, eligible Email/SMS outbox records, consent withdrawal, unverified contacts, email-only consent, invalid/missing IDs, started/past appointments, and commit rollback.

Flutter coverage includes exact request path/method/authentication, no client-supplied tenant identity, current-page refresh, confirmation/decline, loading/disabled actions, success/error presentation, past/started/cancelled guards, duplicate-click prevention, accepted-request timeout reconciliation, a still-confirmed read followed by later cancellation, failed reconciliation, explicit retry using the same ID, failed history refresh after success, and logout during an in-flight request.

During validation:
- Analyzer style findings in the new code were corrected.
- Two initial widget failures exposed off-screen loading/error feedback; bringing feedback into view fixed them.
- A later duplicate-click test asserted before the asynchronous mock had received the request. It now waits on an explicit request-start signal. The final complete suite passes.

## Safety and remaining limitations

- No commits, pushes, branch switches, or changes to main.
- No database migration commands or application startup against an existing database. Existing automated tests create/migrate disposable SQLite in-memory databases as part of their fixtures.
- No migration files or snapshots changed.
- HTTP integration tests use fake Email/SMS senders; Flutter requests use mocked HTTP. No real email/SMS, real transport credentials, production data, or production databases were used.
- End-to-end behavior is covered across HTTP integration and Flutter mocked-flow tests. No manual device/browser session against a running backend was performed.
- Live PostgreSQL HTTP cancellation was not exercised in this environment.
- Device time controls client-side action visibility; server UTC time is the final cancellation guard.
- Reconciliation requires connectivity. The app retains uncertainty within the current session and never treats a still-confirmed read as proof that the original request failed.
- Consent management and phone verification UI/API remain outside this step. Existing opt-in and verified-contact requirements remain unchanged.

## Final git status --short
~~~text
 M backend/DentalApi.IntegrationTests/AppointmentApiTests.cs
 M backend/DentalApi/Controllers/AppointmentsController.cs
 M mobile/lib/live/dental_api.dart
 M mobile/lib/live/live_flow_controller.dart
 M mobile/lib/live/live_patient_flow.dart
?? backend/STEP7_2B1_REPORT.md
?? mobile/test/cancellation_test.dart
~~~
