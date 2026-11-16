# Nexora Dental — Clinical Premium UI / Figma implementation

Design source: https://www.figma.com/design/9l1nCIOPJGiIpVq3aGQZFz

Scope: visual redesign of the Flutter patient app, based on the six latest
390 × 844 editable Figma screens. The design remains a visual specification:
example names, dates and times are **not** copied into LIVE database results.

## Screens covered

| Figma frame | Flutter implementation |
| --- | --- |
| 01 Home | ClinicalPageIntro + ClinicalHero + ClinicalFeatureTile; LIVE can open home while authenticated without losing its session |
| 02 Services | ClinicalChoiceCard + actual `BookingService` data |
| 03 Doctors | ClinicalChoiceCard + actual `BookingDoctor` data |
| 04 Day/time | ClinicalCalendarPicker and existing clinic-aware picker + server slots |
| 05 Review | ClinicalInfoPanel + ClinicalDetail + server-confirmed booking |
| 06 My bookings | LIVE history, pagination, cancellation and notification settings preserved |

Palette: navy `#152E47`, blue `#2879A9`, sky `#E8F4FC`,
paper `#F6F8FA`, muted `#718298`. Roboto with Material 3.

## Safety and behavior

- `main`, backend, migrations and API DTOs were not modified.
- LIVE state remains in `LiveFlowController`; no fake slots or patient
  records are substituted, no authentication is bypassed, and no real
  notification delivery is claimed.
- The inline calendar is a convenient two-week **device-local** date picker;
  use **Өөр огноо сонгох** for the rest of the 30-day range. The backend's
  clinic-local rules remain authoritative and a slot must be fetched again
  for any newly selected date.
- Booking requestId, conflict/uncertain retry behavior, authorization and
  cancellation logic are preserved.
- DEMO still makes no network requests or bookings. It is intentionally
  labeled as a simulation.
- The 6 Figma frames do not include login/profile/verification designs, so
  those existing flows inherit the palette and common controls without
  inventing new authentication behavior.

## Run / verify locally

For the DEMO, from the repository root:

```powershell
git fetch origin
git switch feature/figma-clinical-premium-ui
cd mobile
flutter pub get
flutter analyze
flutter test
flutter run -d chrome
```

For LIVE, use your already-authorized local development API and actual
clinic ID (do not use DEMO IDs as database IDs). See `mobile/README.md`
for API/CORS/secret requirements and setup. As an example only:

```powershell
flutter run -d chrome --web-hostname localhost --web-port 7357 --dart-define=NEXORA_API_BASE_URL=http://localhost:5080 --dart-define=NEXORA_CLINIC_ID=YOUR_AUTHORIZED_CLINIC_ID
```

If you use PowerShell as written, replace `YOUR_AUTHORIZED_CLINIC_ID`
with the numeric authorized test clinic ID.

**Validation limitation:** This branch was written via the GitHub connector.
The assistant environment did not have Flutter/Dart installed, so
`flutter analyze`, `flutter test`, browser visual comparison and live
end-to-end validation have NOT been executed here. Run those checks locally
before merging or deploying, and report any errors for follow-up.

Files: `mobile/lib/app/app_theme.dart`,
`mobile/lib/app/clinical_widgets.dart`,
`mobile/lib/app/patient_flow.dart`,
`mobile/lib/live/live_patient_flow.dart`,
`mobile/test/ux_refinement_test.dart`, and
`mobile/test/clinical_premium_ui_test.dart`.
