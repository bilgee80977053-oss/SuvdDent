# PostgreSQL workflow scripts

These scripts are reviewable automation for the isolated PostgreSQL test and
migration-rehearsal databases. No live database workflow was executed as part
of this change.
Run them only in a PowerShell environment where organizational policy permits
the reviewed scripts; do not lower or bypass execution policy for convenience.
They never accept a database name or host from the caller: the test runner is
fixed to `127.0.0.1:5432/nexora_dental_test`, and the migration workflow is fixed
to `127.0.0.1:5432/nexora_dental_rehearsal`, both using role `nexora_dev`.
Development, production, and any unlisted database names are rejected.

## Automation hardening and offline checks

The shared helper supports Windows PowerShell 5.1. It discovers PostgreSQL 18
tools from `PATH` or the fixed PostgreSQL 18 `bin` locations under Program
Files, and verifies each executable's reported major version before use.
Inspection SQL is restricted to a single `SELECT`, written to a temporary
UTF-8 file without a BOM, and run inside a read-only transaction. Approved
migration SQL is combined with a same-`psql`-session guard that rechecks the
fixed database, role, local endpoint, schema, PostgreSQL version, and exact
ordered current migration history before executing the migration file.

The migration rehearsal fingerprints application rows as canonical JSON data,
sorted by table and row content, and hashes that representation with SHA-256.
This avoids treating nondeterministic `pg_dump` output as a data-change signal;
the separate custom-format backup continues to receive its own archive SHA-256.
The integration runner reads test counts from the structured TRX result file
rather than relying on console summary wording.

Appointment constraint verification checks the exact public constraint names,
owning tables, validated state, definition type and key columns. It requires the
doctor-service FK to use `ON DELETE RESTRICT`, and both GiST exclusions to use
clinic-scoped equality, confirmed-only predicates, overlap operators, and `[)`
time ranges. PostgreSQL truncates the generated FK name with one trailing `~`;
the SQL-name allowlist permits only that optional terminal character while
continuing to reject quotes, whitespace, and SQL syntax.

The reviewed live history contains seven ordered PostgreSQL IDs, ending in
`20261127122656_AddPostgreSqlAppointmentReschedules`. The clinical audit review
overlay declares eight source migrations and the treatment draft nine; live
integration and rehearsal stop before credential entry on both branches. The development fixtures also reject it
before writes. No clinical migration approval is defined. The existing test-result
gate for the seven-migration candidate requires exactly 13 passed results with
no failures or skips; failure details are sanitized before entering the report.

Run the offline automation helper regression checks in a PowerShell environment
where organizational policy permits the test script:

```powershell
.\backend\scripts\tests\PostgreSqlAutomation.Tests.ps1
```

Appointment-integrity and automation validation:

- PowerShell 5.1 parser: all four scripts/modules parsed successfully.
- Offline helper test invocation was blocked by the effective `Restricted`
  execution policy. The policy was not changed or bypassed; run the helper test
  later in an approved PowerShell environment. Cases cover the generated single
  trailing `~`, rejection of multiple terminal tildes and SQL injection, exact
  constraint definitions, the sixth migration, and the 13-test TRX gate.
- Release build: `dotnet build .\backend\NexoraDental.sln --configuration Release
  --no-restore` succeeded with 0 warnings and 0 errors.
- Offline backend tests: 520 passed, 0 failed, 0 skipped using the filter
  `Category!=PostgreSqlIntegration&Category!=PostgreSqlConcurrency`.
- Both SQLite and PostgreSQL `has-pending-model-changes` checks reported no
  pending model changes. Provider SQL was generated and reviewed offline; it was
  not applied to a database.
- Live PostgreSQL integration tests and migrations were not run, and no
  database was accessed.

## PostgreSQL integration tests

From the repository root, run:

```powershell
.\backend\scripts\Invoke-PostgreSqlIntegrationTests.ps1
```

The runner first checks that `PGPASSWORD` and
`NEXORA_PG_TEST_CONNECTION` are absent, then performs an offline Release build.
It requires an explicit confirmation before read-only target preflight and
prompts for the PostgreSQL password as a `SecureString`. It validates the exact
local endpoint, database, role, public schema, PostgreSQL major version 18, and
the ordered EF migration history/product version.

The expected history is exactly the seven PostgreSQL migration IDs in the
repository. The runner accepts only the reviewed four- through seven-migration ordered prefixes. At four migrations it prepares only the
tenant-root migration and requires `APPLY_TENANT_ROOT_FK_MIGRATION`; rerun the
runner after that migration is independently applied to prepare the appointment
integrity migration, which requires the distinct exact token
`APPLY_APPOINTMENT_INTEGRITY_MIGRATION`. The seventh migration adds the
reschedule ledger and requires `APPLY_APPOINTMENT_RESCHEDULE_MIGRATION`. Each approved write rechecks the exact
same-session database identity and migration prefix. The runner never applies a
migration automatically, and it will not execute tests while the history is
incomplete. With all seven migrations applied, it verifies the tenant-root and
appointment-integrity constraints before test approval.

Before running the tests, it asks for a separate `RUN_13_POSTGRESQL_TESTS`
approval. The test filter includes both PostgreSQL integration categories. A
successful result must contain exactly 13 passing tests with zero failures and
zero skips; a changed test count requires script review. The runner temporarily
sets `PGPASSWORD` for `psql` and `NEXORA_PG_TEST_CONNECTION` for the test process,
then removes only the variables it set in `finally`. Passwords and connection
strings are not put in arguments, output, or reports. Provider/test output is
captured and not copied to the report. The secret-free JSON summary is written
under `artifacts\reports`.

The opt-in tests write synthetic rows and perform narrowly scoped cleanup. They
are not read-only tests. Do not approve them unless the database identity and
current data are acceptable for those tests. The runner does not reset
sequences, truncate tables, drop schemas, or clean unrelated records.

## Migration generation and isolated rehearsal

Prepare a migration package and backup without applying schema changes:

```powershell
.\backend\scripts\Invoke-PostgreSqlMigrationWorkflow.ps1 `
  -Mode Prepare `
  -FromMigration 20261102180315_AddPostgreSqlPhoneVerificationChallenges `
  -ToMigration 20261125115303_AddPostgreSqlClinicTenantRootForeignKeys `
  -BackupDirectory 'D:\SuvdDent-PostgreSqlBackups'
```

The backup directory must already exist and be outside the repository. The
workflow prompts securely for credentials, verifies the fixed rehearsal target
and exact migration-history prefix, runs a read-only clinic-root orphan check,
creates a custom-format `pg_dump` backup and SHA-256, generates reviewed EF SQL,
normalizes it to UTF-8 without BOM, and records a pre-migration application-data
SHA-256 fingerprint. The `Prepare` mode does not execute migration SQL.

For an actual isolated rehearsal, use `-Mode Rehearse` with the same required
arguments. The script shows an exact approval prompt before executing generated
SQL against the fixed rehearsal database. It then verifies migration history,
the required named constraints and their validated state, and compares
pre-/post-migration data fingerprints. A data fingerprint change is rejected by
default; `-AllowDataFingerprintChange` requires a second exact approval before
the migration. The workflow does not automatically roll back a failed
post-migration check. Stop, preserve the report and backup, and investigate
before any manual recovery.

Example command for a separately approved rehearsal:

```powershell
.\backend\scripts\Invoke-PostgreSqlMigrationWorkflow.ps1 `
  -Mode Rehearse `
  -FromMigration 20261102180315_AddPostgreSqlPhoneVerificationChallenges `
  -ToMigration 20261125115303_AddPostgreSqlClinicTenantRootForeignKeys `
  -BackupDirectory 'D:\SuvdDent-PostgreSqlBackups'
```

For later migrations, supply their exact ordered migration IDs and explicitly
review the generated SQL, expected history, and `-RequiredConstraint` names.
This project-specific workflow always performs the tenant-root orphan preflight;
adapt it only through a separately reviewed change. Do not point it at
`nexora_dental_dev`, a production database, or an arbitrary database.

To prepare/rehearse the appointment-integrity migration on an isolated rehearsal
database, use the exact ordered fifth-to-sixth migration range and explicitly
require all new PostgreSQL constraints:

```powershell
.\backend\scripts\Invoke-PostgreSqlMigrationWorkflow.ps1 `
  -Mode Prepare `
  -FromMigration 20261125115303_AddPostgreSqlClinicTenantRootForeignKeys `
  -ToMigration 20261126070352_EnforceAppointmentIntegrity `
  -RequiredConstraint @(
    'AK_DoctorServices_ClinicId_DoctorProfileId_ServiceId',
    'FK_Appointments_DoctorServices_ClinicId_DoctorProfileId_Servic~',
    'EX_Appointments_ConfirmedDoctorOccupied_NoOverlap',
    'EX_Appointments_ConfirmedPatientTreatment_NoOverlap'
  ) `
  -BackupDirectory 'D:\SuvdDent-PostgreSqlBackups'
```

`Prepare` performs no schema writes. `Rehearse` still requires its separate
exact write approval and same-session identity/history guard.

## Secret and artifact handling

- Never put passwords or connection strings in command arguments, source,
  configuration files, reports, or shell history.
- Passwords are entered through a secure prompt and held in process memory and
  child-process environment variables only for the operation; both temporary
  environment variables are cleared in `finally`.
- `psql`, `pg_dump`, and test-runner provider diagnostics are suppressed to
  prevent connection details from appearing in logs. The scripts report only a
  stage identifier when a step fails.
- Generated SQL and JSON reports are written under `artifacts`; backups are
  written only to the caller-specified external directory. Review and retain
  those artifacts under the project’s data-handling policy. Do not commit
  database backups or generated reports.
- No script creates roles, grants permissions, applies migrations outside the
  explicit rehearsal/test approval path, or targets the development database.
