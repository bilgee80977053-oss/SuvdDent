# PostgreSQL workflow scripts

These scripts are reviewable automation for the isolated PostgreSQL test and
migration-rehearsal databases. They were not executed as part of this change.
Run them only in a PowerShell environment where organizational policy permits
the reviewed scripts; do not lower or bypass execution policy for convenience.
They never accept a database name or host from the caller: the test runner is
fixed to `127.0.0.1:5432/nexora_dental_test`, and the migration workflow is fixed
to `127.0.0.1:5432/nexora_dental_rehearsal`, both using role `nexora_dev`.
Development, production, and any unlisted database names are rejected.

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

The expected history is exactly the five PostgreSQL migration IDs in the
repository. If the isolated test database has only the first four, the runner
checks the tenant-root orphan preflight and generates UTF-8 SQL without a BOM
for the fifth migration. It requires the exact approval token
`APPLY_TENANT_ROOT_FK_MIGRATION` before applying that SQL. It never applies a
migration automatically. If all five migrations already exist, it verifies the
three validated restrictive clinic-root FKs directly.

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
