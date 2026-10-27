# Nexora Dental mobile — university MVP, STEP 1

Student: В242270028 — А. Билэгсайхан

Assignment: Nexora Dental — Шүдний эмнэлгийн цаг захиалгын аппликейшн

This Flutter application is an **offline, synthetic walkthrough**. It does not
call the backend, authenticate users, create accounts, send verification codes,
store tokens or patient records, reserve appointments, or send email. Every
screen labels its data as a demonstration. No real credentials are requested.

## Run and validate

From the repository root, using the installed Flutter SDK (generated with
Flutter 3.47.7 / Dart 3.13.5):

```powershell
cd mobile
flutter pub get
flutter analyze
flutter test
flutter run -d chrome
# Or select an available Android device/emulator with flutter devices.
```

Android, iOS and web scaffolds are included. iOS native builds require macOS and
Xcode. Device/emulator builds require the corresponding installed toolchains;
widget tests and analysis do not establish native build compatibility.

## Walkthrough

1. Select one of two synthetic clinics.
2. Switch between registration/login placeholders; continue as a synthetic user.
3. Simulate email verification (no code or email is sent).
4. Simulate creating or retrieving that clinic's patient profile.
5. Select a clinic-local service.
6. Read and select an eligible synthetic doctor.
7. Select a frozen example slot (2 November 2026, not live availability).
8. Review the selections and finish the preview. The result explicitly states
   that no appointment or email was created. Restart clears all selections.

Back navigation retains draft choices. Changing clinic clears simulated identity,
verification, profile and all booking selections. Changing service clears the
selected doctor and slot. The preview cannot finish without simulated email
verification and profile setup. These UI guards are **not server authorization**.

## Structure

- `lib/app/`: app entry, shared Material 3 theme, flow shell and in-memory controller.
- `lib/core/widgets/`: reusable selection cards and detail components.
- `lib/data/`: synthetic models/catalog; no API repository or persistence adapter.
- `lib/features/`: clinic, auth/verification, profile, service, doctor and booking screens.
- `test/`: flow guard/reset tests and patient walkthrough widget tests.

No third-party runtime dependencies were added. Only Flutter SDK widgets and the
generated Flutter test/lint development dependencies are used.

## Boundaries for subsequent steps

Real authorization must remain in the backend: derive ClinicId and patient
ownership from validated JWT/database state. The client must never claim email
verification or choose another clinic by changing a request's tenant fields.
The real flow will use the existing clinic registration/login/verification and
patient APIs; STEP 1 intentionally supplies none of these network operations.

Before email-dependent booking, the server must require a verified email.
Appointment persistence and a durable email outbox must commit together; only
send confirmation email **after** the appointment transaction commits. Those
features and provider-specific migrations are outside STEP 1 and require review.

Demo UTC slots use fixed offsets for these two frozen clinic fixtures only.
Production slots must come from the server using a verified clinic timezone;
no device-local clock or fixed-offset calendar logic should authorize bookings.
Do not put API secrets, SMTP credentials, real tokens or real patient data here.
The backend, its migrations and startup migration guard remain unchanged.
