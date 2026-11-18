# PostgreSQL staging fixture — synthetic LIVE rehearsal only

This standalone tool is separate from the production API and the existing SQLite
school fixture. It NEVER runs EF migrations, drops tables/databases, truncates,
resets sequences, deletes existing records, or contacts SMTP/SMS providers.

**Only allowed target**: native PostgreSQL on 127.0.0.1:5432, database
`nexora_restore_check_20261118_201309`, login `nexora_dev`. No CLI override
of the target is accepted. It does not use `ConnectionStrings__Default`, `NEXORA_PG_TEST_CONNECTION`,
or a user-assembled connection string. The only connection secret is supplied
through the private `NEXORA_PG_STAGING_DB_PASSWORD` environment variable.

The three modes are:

- `plan`: read-only verification of database/user, exact migration history,
  exact 17-table inventory and 0 rows in every application table.
- `create`: repeats preflight; with a separate exact opt-in and private patient
  password, locks the table set in one SERIALIZABLE transaction, verifies it is
  still empty, inserts only synthetic clinic/users/doctor/service/patient/hours,
  checks timezone policy and commits atomically. No cleanup or repeated seeding.
  Retain the output clinic ID and patient login for Flutter. On failure, do not
  rerun `create` until inspecting the target.
- `inspect`: read-only identity, migrations, table counts. No editing.

**Preparation on the Windows development workstation (PowerShell)**:

```powershell
cd C:\Users\Surface\SuvdDent
# This tool constructs the fixed target using NpgsqlConnectionStringBuilder.
# Supply only the private database password, not a manually quoted connection string.
$secure = Read-Host "nexora_dev password" -AsSecureString
$ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
try { $pw = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr) }
finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr) }
try {
    $env:NEXORA_PG_STAGING_DB_PASSWORD = $pw
    dotnet run --project backend/DentalApi.PgStagingFixture -c Release --no-build -- plan
    if ($LASTEXITCODE -ne 0) { throw 'Read-only preflight failed.' }
}
finally {
    Remove-Item Env:\NEXORA_PG_STAGING_DB_PASSWORD -ErrorAction SilentlyContinue
    Remove-Variable pw,secure,ptr -ErrorAction SilentlyContinue
}
```

Before `create`, independently review the DB/branch and its backup. The following
command names the required approval but intentionally does NOT set it or create
anything. A future explicitly authorized `create` run must pass
`NEXORA_PG_STAGING_WRITE_OPT_IN=CREATE_SYNTHETIC_PG_STAGING_FIXTURE` and
`NEXORA_PG_STAGING_PATIENT_PASSWORD` as private process environment variables;
the password must be 8–72 UTF-8 bytes and must never be committed, logged or
sent in chat. Remove all fixture environment variables after the one use.

Do not run this on a production, development or integration-test database.
Do not add this fixture project to the deployed API service. Its synthetic emails
end in `example.invalid`; real email/SMS delivery is not part of the rehearsal.
PostgreSQL API startup's separate approval and safe migration history verification
remain required. For Flutter, configure the returned clinic ID explicitly, and
serve a localhost API with CORS permitted for the exact Flutter web origin.

**Build status:** This file and tool were added remotely on the feature branch.
A Windows `dotnet build` / `dotnet run -- plan` must pass before a write is
authorized. A prior backend suite result does not include this new executable.
