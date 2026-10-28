# STEP 5 — isolated development rehearsal

Status: **PREPARED; LIVE END-TO-END UNTESTED / BLOCKED BEFORE FILE-DATABASE WRITES**.
Date: 28 October 2026 (Asia/Ulaanbaatar).

## Inspection and prerequisites

- Branch `feature/school-appointment-mvp`, expected HEAD `c042034` and clean
  starting working tree all matched. No commit, push or branch change performed.
- Read the actual Auth/Patients/Appointments controllers and request/response
  contracts, Flutter LIVE configuration, Program.cs, DbSeeder, provider settings,
  startup configuration validator, scheduling policy, model constraints, existing
  appointment integration fixtures, migration docs/history, configuration docs,
  AppointmentApi.md, AppointmentPersistence.md and mobile README.
- SQLite `AppDbContext` has 11 historical migrations, ending at
  `20261027180600_AddAppointmentFoundation`. PostgreSQL has its separate context
  and history. No migration or model was changed.
- Development startup automatically migrates SQLite; starting the backend before
  validating a target would be unsafe. The PostgreSQL startup guard stays intact.
- DbSeeder alone is insufficient: it creates a clinic, Director and services but
  does not prepare the verified timezone, published doctor, doctor-service/hours,
  verified patient or own profile needed by booking.
- Exact contracts remain unchanged: login uses identifier/password/clinicId;
  verification uses userId/code and returns a receipt rather than a token;
  Patients/me derives ownership from JWT; booking accepts only doctorProfileId,
  serviceId, startUtc and requestId. No tenant or authorization bypass was added.
- No suitable public clinic directory exists. Actual fixture IDs must be read
  after creation, never assumed to be demo IDs 101 or 202.

## Chosen database FILE PATH and safety result

```text
C:\Users\Surface\AppData\Local\NexoraDental\school-test\step5-c042034-20261028.sqlite
```

This exact absolute path was displayed and checked. The file and its ownership
sidecar **do not exist**. It is outside the repository and separate from any
local-dev.db, existing SQLite files and PostgreSQL databases.

Observed before execution:

| Prerequisite | Actual result |
| --- | --- |
| Proposed SQLite file absent | Confirmed |
| `NEXORA_SCHOOL_FIXTURE_WRITE_OPT_IN` supplied | **No** |
| `NEXORA_SCHOOL_PATIENT_PASSWORD` supplied | **No** |
| `NEXORA_PG_TEST_CONNECTION` absent | Confirmed without printing values |

The task explicitly requires stopping before database writes without opt-in.
Consequently no file database was created, opened, migrated or seeded, and no
backend was started. Private passwords must be supplied through the local secure
prompt, never through chat, source, documentation or command-line arguments.

## Exact files changed

| File | Change |
| --- | --- |
| `backend/DentalApi.SchoolFixture/DentalApi.SchoolFixture.csproj` | New standalone .NET 10 console utility referencing existing API/model/migrations |
| `backend/DentalApi.SchoolFixture/Fixture.cs` | New target/opt-in/ownership/configuration guards, synthetic fixture, plan/create/read-only inspect/check-start commands |
| `backend/SchoolRehearsal.ps1` | New Plan/Create/Inspect/Start launcher, explicit write switch, private password prompt, temporary private environment and loopback startup |
| `backend/DentalApi.IntegrationTests/SchoolFixtureTests.cs` | Five new safety and in-memory fixture tests |
| `backend/DentalApi.IntegrationTests/DentalApi.IntegrationTests.csproj` | Reference fixture project so the normal solution build/test compiles and tests it |
| `mobile/README.md` | Short Step 5 status/setup link; existing LIVE implementation retained |
| `mobile/STEP5_REPORT.md` | This report and manual verification procedure |

Production API source, DbSeeder, JWT validation, migrations/snapshots, tenant
constraints, PostgreSQL startup protection, CORS policy, all Flutter source and
historical regression tests are unchanged. The production API does not reference
the fixture project; only the integration tests do.

## Fixture design and write boundaries

The tool accepts only an absolute direct `step5-*.sqlite` child of the current
user's LocalApplicationData/NexoraDental/school-test directory. It rejects linked
or junction ancestors and existing main files, WAL/SHM/journal files or ownership
markers. It has no arbitrary connection-string/provider input. Creation requires
exact opt-in `CREATE_NEW_SQLITE_SCHOOL_FIXTURE` plus a privately supplied patient
password (8+ characters, at most 72 UTF-8 bytes for BCrypt).

After all checks it reserves an ownership marker and the database with atomic
`FileMode.CreateNew`; collisions cannot overwrite files. Only `AppDbContext`
with SQLite options is migrated. Fixture inserts run in one transaction. On
failure, partial artifacts remain for inspection; there is no delete, truncate,
replace, automatic cleanup or in-place retry. Choose a new name only after review.

The complete synthetic fixture contains:

- One clinic named with `NEXORA-SCHOOL-STEP5-SYNTHETIC-ONLY` and a unique run ID.
- An active Director and Doctor user with randomly generated, undisclosed passwords.
- Location MN / province 11 / district 1101, a clearly synthetic address and
  `Asia/Ulaanbaatar` timezone verified by that same active Director.
- One active published doctor profile, one zero-priced test treatment, an active
  30-minute doctor-service association with zero buffers, and 09:00–17:00 working
  hours on every weekday. Tomorrow's clinic-local date supplies future candidates;
  actual availability remains the existing server's responsibility.
- One active preverified Patient `school-patient@example.invalid`, using only the
  privately supplied password hash, and an active synthetic patient profile.

All user emails use `example.invalid`. All names/addresses are explicitly synthetic.
The already-verified flag exists only in this opt-in, standalone fixture, as allowed
by the task. Registration/verification API behavior and production startup are not
changed. The fixture contains no precreated appointment.

Read-only inspection requires a matching ownership marker, all existing migrations
applied, expected synthetic clinic/users/relationships and valid scheduling flags.
It returns actual IDs and a suggested date; it does not assume identifiers. Startup
checks use the actual API JSON, Development user-secrets and environment sources,
including ASPNETCORE_/DOTNET_ host fallbacks, without displaying their values.
They reject PostgreSQL, another file, create-if-missing mode, disabled foreign keys,
public/inherited Kestrel endpoints, extra origins, forwarding or inherited HTTPS
redirect settings. Such conflicts must be resolved privately; no existing config
is overwritten. A port-in-use check precedes server launch.

## Backend startup procedure — PREPARED, NOT EXECUTED

Run from the repository root in a locally approved PowerShell execution environment.
**Current machine execution policy blocked the .ps1 launcher, including Plan.** Its
syntax parsed successfully. Execution policy was not changed or bypassed; script
runtime behavior is UNTESTED. Use an organization/user-approved script execution
or signing workflow rather than weakening that policy.

The console utility itself was built and its safe plan/refusal modes ran:

```powershell
$databaseFile = Join-Path $env:LOCALAPPDATA 'NexoraDental\school-test\step5-c042034-20261028.sqlite'
dotnet build .\backend\NexoraDental.sln
$fixtureTool = '.\backend\DentalApi.SchoolFixture\bin\Debug\net10.0\DentalApi.SchoolFixture.dll'
dotnet $fixtureTool plan $databaseFile
```

Plan prints the resolved path and requires it to be new. It opens no database.
After reviewing the exact target, explicitly opt in and enter the patient password
at the hidden local prompt (never add it as a parameter):

```powershell
.\backend\SchoolRehearsal.ps1 -Mode Create -DatabasePath $databaseFile -AllowFixtureWrites
.\backend\SchoolRehearsal.ps1 -Mode Inspect -DatabasePath $databaseFile
.\backend\SchoolRehearsal.ps1 -Mode Start -DatabasePath $databaseFile -AllowFixtureWrites
```

**These three commands have NOT been executed here.** Inspect reports the real
ClinicId, DoctorProfileId, ServiceId, PatientId, suggested date and appointment
count. If creation fails, preserve the partial file/marker; do not delete or retry
against the same path. The tool refuses subsequent creation against an existing
fixture. Start is a separate authorization for the existing fixture's runtime writes.

The launcher temporarily configures only its process/child environment, then
restores previous values on exit:

| Setting | Rehearsal value/behavior |
| --- | --- |
| `Database__Provider` | `Sqlite` |
| `ConnectionStrings__Default` | Exact selected file, Mode=ReadWrite, Pooling=False, Foreign Keys=True; never printed |
| `DOTNET_ENVIRONMENT`, `ASPNETCORE_ENVIRONMENT` | `Development` |
| URL binding | `http://127.0.0.1:5018`, loopback only, explicit URL variables; no launch profile |
| `AllowedHosts` | `127.0.0.1;localhost` |
| `Cors__AllowedOrigins__0` | Only `http://localhost:7357`; extra inherited origins cause refusal |
| JWT keys | Separate runtime cryptographic random keys, private environment only; distinct issuer/audience pairs |
| SMTP fields required by unchanged validator | Loopback port 9 and synthetic sender, undisclosed random password; no SMTP call is made |

Real SMTP is out of scope. The loopback SMTP settings are deliberately nonfunctional;
this preverified login/booking rehearsal never invokes an email sender or passes
`--test-email`. No validation rule is removed. Do not test real registration/email
on this fixture. The existing HTTPS middleware remains; with no configured HTTPS
port it may warn rather than redirect during this loopback-only HTTP rehearsal.
Any inherited HTTPS redirect configuration causes startup refusal for review.

The API host/port above is the **chosen configuration**, not an observed listening
server. Startup first verifies the existing file has no pending migrations, then
runs the unchanged API DLL from its project directory. Ctrl+C stops it without
removing its database. Each start generates new JWT keys; log in again after restart.

## Flutter startup and manual verification — ALL UNTESTED

There was no authorized file fixture/server, so neither browser automation nor
manual live UI testing was attempted. Compilation/in-memory tests do not count as
Flutter → API → file-database success. The following is an exact manual checklist
for the next authorized run, using the existing app unchanged.

In a second terminal, read the **actual** ClinicId reported by Inspect; then:

```powershell
$clinicId = [int](Read-Host 'Actual ClinicId from owned fixture inspection')
cd mobile
flutter run -d chrome --web-hostname localhost --web-port 7357 --dart-define=NEXORA_API_BASE_URL=http://127.0.0.1:5018 --dart-define=NEXORA_CLINIC_ID=$clinicId
```

The exact browser origin is `http://localhost:7357`. Use debug mode for local HTTP.
Do not change URLs to public hosts, weaken TLS or allow wildcard CORS.

| Check | Manual action and evidence to record | Current result |
| --- | --- | --- |
| 1 | Open app; confirm LIVE banner and actual configured fixture clinic ID | UNTESTED |
| 2 | Choose clinic, log in as `school-patient@example.invalid` using the private password | UNTESTED |
| 3 | Load own synthetic profile; verify GET Patients/me 200 | UNTESTED |
| 4 | Load the synthetic treatment from GET appointments/services 200 | UNTESTED |
| 5 | Load its associated published doctor; check serviceId query and 200 | UNTESTED |
| 6 | Choose the suggested tomorrow clinic-local date and refresh slots; verify returned timezone and 200 | UNTESTED |
| 7 | Select a future server-returned slot and review | UNTESTED |
| 8–9 | Submit once; verify POST appointments **201**, Location header and matching returned appointment ID/details | UNTESTED |
| 10 | Open My appointments; verify own GET page includes that ID | UNTESTED |
| 11 | Stop API with Ctrl+C, leave SQLite/marker untouched, run identical Start command with same databaseFile | UNTESTED |
| 12 | Log out in Flutter, sign in again; restarted server has fresh JWT keys | UNTESTED |
| 13 | My appointments must still include the original ID/start/end/status; read-only Inspect count must be unchanged | UNTESTED |
| 14 | Replay the identical four-field request/body/requestId; require HTTP **200** and original ID | UNTESTED |
| 15 | Send the same doctor/service/start with a new UUID; require **409** and no extra appointment | UNTESTED |

Use browser Network tools locally to check status and the **nonsecret booking
request body only**. Do not export HAR files, tokens, Authorization headers or login
bodies. Keep the original doctorProfileId, serviceId, startUtc and requestId in
memory for replay, plus the returned appointment ID. Record sanitized status/IDs
only. Do not store credential-containing screenshots or transcripts.

After the Flutter-created booking and server restart, the following optional
PowerShell API checks make replay/conflict evidence reproducible. They complement
the UI checks; they cannot establish that the Flutter booking itself succeeded.
Keep the backend running in its own terminal. Enter the original values from the
booking body's four fields, not a newly generated request ID:

```powershell
$baseUrl = 'http://127.0.0.1:5018'
$clinicId = [int](Read-Host 'Actual fixture ClinicId')
$expectedAppointmentId = [int](Read-Host 'Appointment ID returned by Flutter booking')
$intent = @{
    doctorProfileId = [int](Read-Host 'Original doctorProfileId')
    serviceId = [int](Read-Host 'Original serviceId')
    startUtc = (Read-Host 'Original startUtc with Z/offset')
    requestId = (Read-Host 'Original requestId UUID')
}
$privatePassword = Read-Host 'Private synthetic Patient password' -AsSecureString
$pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($privatePassword)
try {
    $loginBody = @{ identifier = 'school-patient@example.invalid'; clinicId = $clinicId; password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) } | ConvertTo-Json
    $auth = Invoke-RestMethod -Uri "$baseUrl/api/Auth/login" -Method Post -ContentType 'application/json' -Body $loginBody -TimeoutSec 20
} finally {
    $loginBody = $null
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
    $privatePassword.Dispose()
}
try {
    $headers = @{ Authorization = 'Bearer ' + $auth.token }
    $mine = Invoke-RestMethod -Uri "$baseUrl/api/appointments/me?page=1&pageSize=20" -Headers $headers -TimeoutSec 20
    if (@($mine.items | Where-Object { $_.id -eq $expectedAppointmentId }).Count -ne 1) { throw 'FAIL: original appointment missing after restart.' }
    $replay = Invoke-WebRequest -UseBasicParsing -Uri "$baseUrl/api/appointments" -Method Post -Headers $headers -ContentType 'application/json' -Body ($intent | ConvertTo-Json) -TimeoutSec 20
    if ([int]$replay.StatusCode -ne 200 -or ($replay.Content | ConvertFrom-Json).id -ne $expectedAppointmentId) { throw 'FAIL: replay did not return the original appointment with 200.' }
    Write-Output 'PASS: original appointment persisted and identical request replay returned 200.'
    $conflictIntent = $intent.Clone()
    $conflictIntent.requestId = [guid]::NewGuid().ToString()
    $conflictStatus = 0
    try {
        $conflictingResponse = Invoke-WebRequest -UseBasicParsing -Uri "$baseUrl/api/appointments" -Method Post -Headers $headers -ContentType 'application/json' -Body ($conflictIntent | ConvertTo-Json) -TimeoutSec 20
        $conflictStatus = [int]$conflictingResponse.StatusCode
    } catch {
        if ($null -eq $_.Exception.Response) { throw 'FAIL: transport error during conflict check.' }
        $conflictStatus = [int]$_.Exception.Response.StatusCode
    }
    if ($conflictStatus -ne 409) { throw 'FAIL: expected HTTP 409.' }
    $after = Invoke-RestMethod -Uri "$baseUrl/api/appointments/me?page=1&pageSize=20" -Headers $headers -TimeoutSec 20
    if ($after.items.Count -ne $mine.items.Count) { throw 'FAIL: appointment count changed during replay/conflict checks.' }
    Write-Output 'PASS: conflicting booking returned 409 without adding a record.'
} finally {
    $auth = $null; $headers = $null
}
```

Do not execute these against any server other than the verified owned fixture.
Keep all secrets in memory, avoid transcription, and do not display `$auth`, headers
or loginBody. If a check fails, record only sanitized status/IDs and stop; do not
relax authentication, timezone, migrations or conflict protections to make it pass.

## Validation actually performed

| Check | Observed result |
| --- | --- |
| `dotnet build .\backend\NexoraDental.sln` | PASS, 0 warnings / 0 errors; fixture utility compiled via test-project reference |
| `dotnet test .\backend\NexoraDental.sln` | PASS: **349 passed, 12 skipped, 0 failed** (361 total) |
| PostgreSQL opt-in absent | Confirmed before the normal suite; all 12 PostgreSQL runtime tests skipped |
| `flutter analyze` from mobile | PASS, no issues |
| `flutter test` from mobile | PASS, **44 tests** |
| Console fixture `plan` for exact chosen path | PASS, target new; no connection/writes |
| Console fixture `create` without opt-in | Expected refusal, exit 2; target and all sidecars still absent |
| PowerShell launcher syntax parsing | PASS |
| PowerShell launcher execution | BLOCKED by local execution policy; policy unchanged |
| `git diff --check`, status and stat | Checked; whitespace clean after normalization |

The five new backend tests cover missing opt-in/password, absolute direct-child
path guards, existing main/sidecar preservation, safe startup configuration
(refusing extra origins/public binding/other file/provider), and a complete
synthetic fixture exercised against the existing booking service in **in-memory
SQLite**. The last test applies the correct 11 SQLite migrations, validates the
patient password hash, and obtains service result codes 201, 200 replay and 409
conflict with exactly one stored appointment. These are service/in-memory test
results, **not observed HTTP responses from a live file-backed API**.

Database connection/data-write accounting: normal integration tests opened
isolated in-memory SQLite connections and wrote synthetic rows. A safety test
created and removed only its own plain-text sentinel files in a unique temporary
directory (not SQLite databases). Build outputs were generated. **No connection
to an existing/file-backed fixture or PostgreSQL database was opened; no persistent
SQLite fixture data or schema was written.** No real patient information was used.

## Remaining blockers and stopping point

1. Explicit fixture write authorization and a locally provided private patient
   password are missing. Do not infer them from successful source-file approvals.
2. The PowerShell execution policy blocked the launcher. It needs an approved
   script execution/signing environment; no policy change was made here.
3. Therefore fixture creation, full read-only ownership inspection, real backend
   startup/binding/CORS, browser UI requests, HTTP 201, file persistence after
   restart, HTTP 200 replay and HTTP 409 conflict remain **UNTESTED**.
4. Native Android and real SMTP/email verification are not part of this rehearsal.

Next: review these changes, approve the exact disposable target/write operation,
use an approved local shell, supply the private password through its hidden prompt,
create once, inspect actual IDs, then follow the startup and 15-check manual sequence.
Do not retry Create on the existing file, delete partial files, reuse local-dev.db,
change PostgreSQL guards, connect to production, commit or push.

Git: still on `feature/school-appointment-mvp` at `c042034`, with two modified tracked
files and five new unstaged files listed above. No production feature or previous
commit was replaced. Stopped at the STEP 5 write prerequisite for review.
