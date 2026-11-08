# Isolated Nexora Dental university DEMO

Preparation is separate from API startup. It creates a fresh directory and SQLite
file, migrates ONLY that exclusively reserved new file, seeds fictional data and
books the sample through AppointmentService. No historical migrations, schema,
public API DTOs, authorization policies or MailKit EmailService are changed.

## Prepare on Windows

From the repository root, use a root outside the repository named NexoraDental-Demos:

```powershell
$patientSecret = Read-Host 'Choose a private DEMO patient password' -AsSecureString
$directorSecret = Read-Host 'Choose a private DEMO Director password' -AsSecureString
.\backend\demo\Prepare-Demo.ps1 -Root 'C:\Users\Surface\Desktop\NexoraDental-Demos' -PatientPassword $patientSecret -DirectorPassword $directorSecret
```

Passwords must have at least 12 characters and at most 72 UTF-8 bytes. The wrapper
can also generate them if these optional parameters are omitted. It saves them
and independent random Clinic/Platform JWT keys in a DPAPI-encrypted private file
with directory ACL inheritance disabled and access granted to the current Windows
user. The output contains paths only. Never print decrypted settings, put them in
Flutter source, send them to Git, or copy them into the manifest. DPAPI settings
belong to this Windows user/machine; keep them private. The wrapper restores
process environment variables after completion, and never deletes old artifacts.

For other hosts, the CLI accepts NEXORA_DEMO_ROOT, NEXORA_DEMO_PATIENT_PASSWORD,
NEXORA_DEMO_DIRECTOR_PASSWORD through process-private environment variables:
`dotnet run --project backend/DentalApi -c Release --no-launch-profile -- --prepare-demo`.
The CLI does not generate JWT keys or a credential store; the Windows wrapper does.
No default DB path is accepted. Failure retains incomplete files for inspection;
an incomplete directory cannot be started. Re-running creates a different directory.

## Dataset and instructor scenario

The manifest is non-secret: IDs, schema fingerprint, preparation time, sample
appointment and nine candidate slots only. Data contains one fictional clinic,
one Director, three active published doctors, five services, seven doctor-service
relationships, 30 Mon-Fri working intervals, one email-verified synthetic global
PlatformAccount, one clinic patient/link created by booking, one appointment and
two simulated notification intents. All contact emails end in example.invalid;
no phone numbers, medical history, real addresses or real people are used.

Location MN/11/1101 is the existing Ulaanbaatar/Baganuur reference; the address is
explicitly fictional. ClinicLocationService validates it and records a synthetic
Director timezone confirmation for Asia/Ulaanbaatar. This is not proof of any real
contact verification. Authentication still uses normal BCrypt and real JWT validation.
The patient identifier is patient@nexora.example.invalid; Director uses
director@nexora.example.invalid and the private chosen password. Doctor passwords
are random and not retained; doctor sign-in is outside this dataset scenario.

Dates use the injected clock converted to clinic time, then the NEXT working day.
The initial consultation is at 09:10. Candidate fillings at 10:10, cleaning at
11:10, extraction at 14:10 are provided over the next three working days. They
are NOT reservations or a free-slot API. Fresh booking-options versions and POST
conflict checks remain authoritative. Prepare shortly before the instructor demo;
old candidates can become past times, and content expires after 24h.

## Start without migrations

Use the two paths printed during preparation:

```powershell
.\backend\demo\Start-Demo.ps1 -DemoDirectory '<absolute generated demo directory>' -PrivateSettingsFile '<absolute DPAPI settings file>'
```

Add `-ValidateOnly` to check private configuration, manifest, migration history,
model/snapshot consistency and schema without starting a server or running migrations.

The wrapper selects Demo environment and --no-launch-profile, using HTTPS localhost
by default. Existing launch profiles choose Development and must not be used.
For a future Android emulator/phone, configure an appropriate reachable HTTPS URL
and trusted certificate separately; do not bypass authentication or TLS validation.

Runtime requires Demo:Root and Demo:DatabasePath, opens SQLite in ReadWrite (never
Create) mode, and refuses absent manifests, symlinks, wrong locations, migration
history/schema fingerprint mismatches, missing overlap triggers, broken integrity
or an incomplete sample dataset. Demo startup performs NO migration and NO seeding.
ConnectionStrings:Default is ignored in Demo. Existing non-Demo/non-Testing startup
behavior remains unchanged; do not use it for this database.

appsettings.Demo.json contains public issuer/audience names, logging and opt-in DEMO
worker configuration only. Keys are process-private. Nonempty SMTP settings are
rejected before serving requests. Platform registration/recovery delivery remains
disabled; the pre-provisioned synthetic patient logs in normally. No real Email/SMS
transport is registered by preparation. Never pass --test-email.

The preparation command runs the DEMO processor once: two initial rows are
DemoGenerated, never Delivered. In Demo runtime the worker is enabled by default,
processing later bookings every ten seconds. It erases content older than 24h when
running; metadata remains for deduplication. An offline worker cannot enforce
wall-clock deletion, and backups/private settings need separate retention controls.

The future Flutter app must log in, obtain fresh booking-options, POST a candidate
with a new UUID Idempotency-Key, and GET the saved appointment. Retrying the same
payload/key must not add rows. Notification content has no public/Flutter endpoint;
use only trusted local inspection of this fictional database for now. This phase
does not implement Flutter, slot discovery or a notification preview API.

## Validation

Run `dotnet test backend/NexoraDental.sln -c Release` and
`dotnet build backend/NexoraDental.sln -c Release`.
DemoPreparationTests provisions ONLY new uniquely named test DEMO databases under
the OS temporary directory, never development/production files; tests retain these
artifacts. Existing migration/notification tests still use in-memory SQLite.
No reset, database overwrite, automatic cleanup, commit or push is performed.
