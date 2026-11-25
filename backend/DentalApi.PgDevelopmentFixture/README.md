# PostgreSQL development fixture

This standalone console utility is for the single, local development database
`nexora_dental_dev`. It accepts no connection string or target arguments and
never falls back to API/test configuration.

## Safety boundaries

- Fixed target: PostgreSQL at `127.0.0.1:5432`, database `nexora_dental_dev`,
  login `nexora_dev`, `public` schema, PostgreSQL major version 18.
- The database password is read only from the private process environment
  variable `NEXORA_PG_DEVELOPMENT_DB_PASSWORD`.
- All modes perform a read-only preflight of database/user/server identity,
  PostgreSQL migration history, exact EF table/column inventory, and row counts.
  The public schema must contain exactly 18 tables: 17 application tables and
  `__EFMigrationsHistory`, whose two columns are checked separately. Migration
  history is not considered application data.
- `plan` and `create` refuse any nonempty application table. `inspect` reports
  counts without writing.
- `create` additionally requires the exact approval value
  `NEXORA_PG_DEVELOPMENT_WRITE_OPT_IN=CREATE_SYNTHETIC_PG_DEVELOPMENT_FIXTURE`
  and a private `NEXORA_PG_DEVELOPMENT_ACCOUNT_PASSWORD` of 12-72 UTF-8 bytes.
- Before inserts, `create` takes one SERIALIZABLE transaction, locks every
  allowlisted table plus migration history, repeats identity/schema/emptiness
  checks, and inserts all fixture rows atomically. It neither migrates nor
  cleans up data. A failed or interrupted run must be inspected before retrying.
- Fixture accounts use BCrypt and `example.invalid` email addresses. All use
  the private account password supplied at create time. Email is marked verified
  for local authentication only; no email, SMS, or other delivery is invoked.
- The clinic, services, staff, and patients are explicitly synthetic. Service
  prices are zero and descriptions disclaim clinical use.

## Offline validation

From the repository root, build and run the test suite with the real PostgreSQL
integration opt-in absent:

```powershell
if (Test-Path Env:NEXORA_PG_TEST_CONNECTION) {
    throw 'Remove NEXORA_PG_TEST_CONNECTION before offline validation.'
}
dotnet build backend/DentalApi.PgDevelopmentFixture/DentalApi.PgDevelopmentFixture.csproj -c Release
dotnet test backend/NexoraDental.sln -c Release
```

These commands do not execute the fixture, connect to PostgreSQL, or apply
migrations to a persistent database. Existing tests may run migration code
against isolated in-memory SQLite databases. Do not use
`dotnet run ... plan|create|inspect` until separately authorized to connect to
the local development database.
