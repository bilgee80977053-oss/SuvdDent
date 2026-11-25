# PostgreSQL cross-tenant development fixture

This is a standalone console project, separate from the API and the existing
single-clinic PostgreSQL fixture. It is restricted in code to PostgreSQL 18 at
`127.0.0.1:5432`, database `nexora_dental_dev`, login `nexora_dev`, and schema
`public`. It accepts no connection-string or target override.

## What it checks and creates

`plan`, `create`, and `inspect` all use read-only preflight checks for database
identity, exact four-migration history, the public schema inventory, and table
counts. The schema must have the expected 17 application tables plus the EF
migration-history table. The preflight requires Clinic 1 to have the exact
original synthetic clinic name, nine expected `development.*@example.invalid`
users with their expected names and roles, three doctor profiles, five
patients, six named synthetic services, six doctor-service links, 15 working
hour rows, and exactly three appointments. It also checks global counts so
records outside Clinic 1 and the known second-clinic fixture are rejected.
Notification outbox rows are the exception: rows from previous appointment
tests are allowed only when they belong to one of Clinic 1's three baseline
appointments; outbox data remains part of the Clinic 1 fingerprint.

The inspector intentionally requires this original three-appointment baseline;
it is not a general-purpose database inspector. It verifies a fingerprint of
all Clinic 1 tenant records. Sensitive row contents and fingerprints are never
printed.

`create` adds only a second synthetic clinic with one director, one doctor, one
patient, one service, one doctor-service assignment, and five weekday working
hour rows. Accounts use BCrypt and `example.invalid` addresses. All use the
private account password supplied for the run. The service is a zero-price
placeholder. No appointment, notification, verification challenge, SMS, or
email is created.

Before writing, `create` requires an exact write opt-in, starts one SERIALIZABLE
transaction, locks the allowlisted tables and migration-history table, then
revalidates identity, migrations, schema, fixture uniqueness, and Clinic 1's
fingerprint. It checks the fingerprint again after inserts and before commit.
Any failure rolls back the transaction. Existing Clinic 1 records are never
updated or deleted.

## Tenant and endpoint boundaries

| Endpoint | Access and tenant scope |
| --- | --- |
| `GET /api/Services?clinicId={id}` | Intentionally public catalog; the explicit clinic ID selects the catalog. It does not expose private patient or appointment data. |
| `GET /api/appointments/services` and `/api/appointments/doctors` | Patient-authenticated; scope comes from the JWT clinic claim, not a caller-supplied clinic ID. |
| `GET /api/Doctors` and doctor self-service routes | Authenticated; scope comes from the JWT clinic claim. |
| `/api/appointments/*` | Patient role required; ownership and clinic are derived from validated JWT claims and checked in service queries. |
| `/api/admin/*` | Director role required; operations use the authenticated director's clinic claim and tenant-filtered queries. |

Do not treat the public `ServicesController` catalog as authorization for
private patient, scheduling, appointment, or administrative operations.

## Configuration and manual commands

Supply these values privately in the process environment; do not put credentials
in command history, source files, appsettings, or chat:

- `NEXORA_PG_CROSS_TENANT_DB_PASSWORD`: the local `nexora_dev` database password.
- `NEXORA_PG_CROSS_TENANT_ACCOUNT_PASSWORD`: a private synthetic login password
  of 12-72 UTF-8 bytes.
- `NEXORA_PG_CROSS_TENANT_WRITE_OPT_IN`: for `create` only, exactly
  `CREATE_SYNTHETIC_PG_CROSS_TENANT_FIXTURE`.

From the repository root, the manual review commands are:

```powershell
dotnet build backend\DentalApi.PgCrossTenantFixture\DentalApi.PgCrossTenantFixture.csproj -c Release
dotnet run --project backend\DentalApi.PgCrossTenantFixture -c Release -- plan
dotnet run --project backend\DentalApi.PgCrossTenantFixture -c Release -- inspect
```

Only after separate review and explicit authorization, set the exact write
opt-in and run:

```powershell
$env:NEXORA_PG_CROSS_TENANT_WRITE_OPT_IN = 'CREATE_SYNTHETIC_PG_CROSS_TENANT_FIXTURE'
dotnet run --project backend\DentalApi.PgCrossTenantFixture -c Release -- create
```

Remove all three fixture environment variables after authorized use. The
commands above are documentation only; do not run them as part of offline
validation.

## Rollback and offline validation

No migrations, schema changes, cleanup, deletes, truncation, or sequence resets
are implemented. An error before commit rolls back all inserts atomically. A
successful commit has no automatic rollback command: inspect and review any
future cleanup separately, targeting only verified fixture-owned rows. Never
delete Clinic 1 data or use a broad tenant/data purge.

Offline checks use only the integration test project's isolated SQLite
databases:

```powershell
if (Test-Path Env:NEXORA_PG_TEST_CONNECTION) {
    throw 'Remove NEXORA_PG_TEST_CONNECTION before offline validation.'
}
dotnet build backend\NexoraDental.sln -c Release
dotnet test backend\NexoraDental.sln -c Release
```

Do not run `plan`, `inspect`, or `create` during offline validation; those modes
require a PostgreSQL connection even though `plan` and `inspect` are read-only.
