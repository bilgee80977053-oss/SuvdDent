# EF Core migrations and SQL review artifacts

Run commands from the repository root with .NET 10 and the repository-compatible
EF Core 10 tooling. All commands below generate or inspect local files/models;
they do not apply migrations. Always specify `--context`: there are two contexts.

**SQLite SQL scripts must NEVER be applied to PostgreSQL.** Provider-specific
SQL, migrations and snapshots are separate even though the contexts share models.

## Source of truth and provider separation

| Provider | Context | EF migration directory | Model snapshot |
| --- | --- | --- | --- |
| SQLite | `AppDbContext` | `backend/DentalApi/Migrations/` | `AppDbContextModelSnapshot.cs` |
| PostgreSQL | `PostgreSqlAppDbContext` | `backend/DentalApi/Migrations/PostgreSql/` | `PostgreSqlAppDbContextModelSnapshot.cs` |

The C# migrations, their `.Designer.cs` target models and the corresponding
snapshot are the schema history. SQL files are derived review/test artifacts,
not a second independently maintained migration system. Preserve all historical
C# migrations. Do not edit snapshots by hand; for future approved model changes,
let EF generate new migrations and update the corresponding snapshots. This
cleanup modifies neither migration C# files nor snapshots.
A shared model change may require a new migration in each provider's set. Review
both providers separately; never move SQLite migrations into the PostgreSQL set.

SQLite has ten historical migrations, from `20261021062135_initialCreate` through
`20261026131551_AddPlatformAccounts`. PostgreSQL starts independently at
`20261027132306_PostgreSqlBaseline`; it does not replay SQLite history or import
SQLite data. Its baseline is for a new PostgreSQL schema, not an upgrade script
for an existing populated database.

## SQLite workflow

Inspect local history without connecting to a database, and check whether the
current model differs from its snapshot:

```powershell
dotnet ef migrations list --no-connect --context AppDbContext --project backend/DentalApi --startup-project backend/DentalApi
dotnet ef migrations has-pending-model-changes --context AppDbContext --project backend/DentalApi --startup-project backend/DentalApi
```

For a future approved model change, replace `DescribeSqliteChange` with a
descriptive name and generate a new migration in the existing SQLite directory:

```powershell
dotnet ef migrations add DescribeSqliteChange --context AppDbContext --output-dir Migrations --project backend/DentalApi --startup-project backend/DentalApi
```

Inspect the generated Up/Down operations, designer and snapshot diff before
accepting the migration. Pay particular attention to SQLite table rebuilds,
foreign-key PRAGMAs, data copying, indexes, defaults and tenant constraints.

## PostgreSQL workflow

Use the dedicated context and directory; changing `Database__Provider` alone is
not a substitute for selecting the correct design-time context:

```powershell
dotnet ef migrations list --no-connect --context PostgreSqlAppDbContext --project backend/DentalApi --startup-project backend/DentalApi
dotnet ef migrations has-pending-model-changes --context PostgreSqlAppDbContext --project backend/DentalApi --startup-project backend/DentalApi
```

For a future approved model change, replace the example name:

```powershell
dotnet ef migrations add DescribePostgreSqlChange --context PostgreSqlAppDbContext --output-dir Migrations/PostgreSql --project backend/DentalApi --startup-project backend/DentalApi
```

Review native identity, UUID, timestamp and collation mappings, constraints and
transaction behavior. Never recreate, edit or replace the existing PostgreSQL
baseline to accommodate later changes.

## Generate SQL for review

`migrations script FROM TO` includes migrations after FROM through TO. `0` means
before the first migration. Use explicit IDs and the intended provider context.
Write new review output outside the historical fixture paths; `artifacts/` is
ignored by Git. SQL generation is not permission to execute the result.

```powershell
New-Item -ItemType Directory -Force -Path artifacts/sql/Sqlite, artifacts/sql/PostgreSql | Out-Null
# Historical SQLite scheduling upgrade only:
dotnet ef migrations script 20261026104238_AddDoctorProfiles 20261026120838_AddSchedulingFoundations --context AppDbContext --project backend/DentalApi --startup-project backend/DentalApi --output artifacts/sql/Sqlite/AddSchedulingFoundations.sql
# Complete SQLite history as of Phase 2 STEP 5:
dotnet ef migrations script 0 20261026131551_AddPlatformAccounts --context AppDbContext --project backend/DentalApi --startup-project backend/DentalApi --output artifacts/sql/Sqlite/FullHistory.sql
# Independent PostgreSQL baseline:
dotnet ef migrations script 0 20261027132306_PostgreSqlBaseline --idempotent --context PostgreSqlAppDbContext --project backend/DentalApi --startup-project backend/DentalApi --output artifacts/sql/PostgreSql/Baseline.sql
```

SQLite does not support EF's idempotent migration script generation; use an
explicit known FROM/TO range. PostgreSQL supports `--idempotent`, which checks EF
migration history. Idempotency does not make a script safe for an arbitrary
schema, a different provider or a database without trustworthy migration history.

## Retained SQL inventory

All four scripts below were generated for SQLite with EF Core 10.0.13. The tenant
integrity script also contains handwritten preflight SQL from its C# migration.

| Artifact relative to this directory | Source migration / FROM -> TO | Test dependency |
| --- | --- | --- |
| `Archive/Sqlite/AddPatientUserTenantIntegrity.sql` | `20261026074904_AddUserTokenVersion` -> `20261026100808_AddPatientUserTenantIntegrity` | No file consumer; archived review record only. `TenantIntegrityTests` exercises the C# migration directly. |
| `AddSchedulingFoundations.sql` | `20261026104238_AddDoctorProfiles` -> `20261026120838_AddSchedulingFoundations` | `SchedulingMigrationTests` compares generated SQL and executes this file in memory. |
| `AddClinicLocationVerification.sql` | `20261026120838_AddSchedulingFoundations` -> `20261026124849_AddClinicLocationVerification` | `ClinicLocationMigrationTests` compares generated SQL and executes this file in memory. |
| `AddPlatformAccounts.sql` | `20261026124849_AddClinicLocationVerification` -> `20261026131551_AddPlatformAccounts` | `PlatformMigrationTests` compares generated SQL and executes this file in memory. |

Only the unreferenced tenant integrity review artifact is archived. Its contents
are preserved byte for byte. The other three files remain in their original
locations because they are active historical test fixtures. Do not hand-edit,
relocate or overwrite them as part of generating SQL for a new migration.
There is no checked-in PostgreSQL `.sql` artifact; generate it from its C# history.

## Migration safety and startup behavior

- SQLite automatically applies pending migrations on application startup outside
  `Testing`. Starting the API can therefore change the configured SQLite database.
- PostgreSQL startup migration protection remains enabled: outside `Testing`,
  startup rejects PostgreSQL before migration/seeding. A baseline on disk does
  not authorize disabling that guard.
- `Testing` skips application startup migration/seeding. Historical tests explicitly
  apply SQLite migrations only to their isolated in-memory databases.
- Both design-time factories ignore application configuration: SQLite uses an
  in-memory connection; PostgreSQL uses credential-free model-only settings.
  The inspection/generation commands above do not start the web application.
  Do not pass real connection strings or treat `database update` with these
  factories as the documented deployment workflow.
- Never run SQLite SQL on PostgreSQL or PostgreSQL SQL on SQLite. SQLite PRAGMAs,
  AUTOINCREMENT, affinities, NOCASE and table rebuild SQL are provider-specific.
- Before a separately authorized rollout, verify the provider and migration
  range, review Up/Down and generated SQL, take a consistent backup, and rehearse
  on an explicitly authorized isolated copy. Compare rows, schema, indexes,
  constraints and tenant integrity. A Down migration can destroy data.
- Never include credentials in source, SQL artifacts, command history or logs.
  Keep deployment authorization separate from generating review files.

For the STEP 5 validation, require `NEXORA_PG_TEST_CONNECTION` to be absent before
running `dotnet build backend/NexoraDental.sln`, `dotnet test backend/NexoraDental.sln`
and `git diff --check`. The ten opt-in PostgreSQL tests should be skipped, not
reported as passed. Remaining tests use in-memory SQLite or offline checks.
No file-backed or real database migration is part of this cleanup.
