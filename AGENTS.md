# Development agent guide

## Repository map

- `backend/NexoraDental.sln`: .NET 10 solution.
- `backend/DentalApi/`: ASP.NET Core API, EF Core contexts/migrations, controllers,
  application services, authentication and notification processing.
- `backend/DentalApi.IntegrationTests/`: xUnit/WebApplicationFactory tests. Most
  tests use isolated in-memory SQLite; PostgreSQL integration/concurrency tests
  require an explicit opt-in and a disposable approved target.
- `backend/DentalApi/Migrations/`: SQLite migration history and snapshot.
- `backend/DentalApi/Migrations/PostgreSql/`: independent PostgreSQL history and
  snapshot. Always choose the matching EF context.
- `backend/scripts/`: guarded PostgreSQL test/rehearsal tooling and offline helper
  tests.
- `mobile/`: Flutter demo and explicitly configured one-clinic LIVE booking UI.

See `DEVELOPMENT_CHECKPOINT.md` for the verified state and `DEVELOPMENT_ROADMAP.md`
for prioritized work. API and data contracts are documented beside the backend.

## Working rules

- Preserve unrelated and pre-existing work. Do not remove untracked local files
  as cleanup. In particular, do not add local editor settings or standalone
  coursework SQL files unless a task specifically requires them.
- Do not commit credentials, real connection strings, real patient data, private
  exports, database backups, or generated `artifacts/`.
- The API derives clinic scope from validated identity and stored ownership.
  Never trust a client-supplied tenant ID or payment status.
- Clinic and Platform authentication are separate. Do not mix their token
  schemes or weaken database-backed claim validation.
- PostgreSQL startup is guarded and does not migrate or seed. SQLite can apply
  migrations automatically outside `Testing`; never start the API against an
  unknown or production database.
- Do not apply migrations, run the opt-in PostgreSQL tests, or use any
  write-capable database workflow without explicit operator approval. Do not
  deploy, modify live patient data, reset a database, or merge this feature
  branch into `main` as part of routine development.
- Do not apply SQLite SQL to PostgreSQL or PostgreSQL SQL to SQLite. Preserve
  historical migrations and generated SQL fixtures. Generate migrations with
  the correct context and review both provider histories independently.

## Offline validation

Confirm these variables are absent before offline tests. Do not print their
values:

```powershell
if (Test-Path Env:NEXORA_PG_TEST_CONNECTION) { throw 'Remove PostgreSQL test opt-in before offline validation.' }
if (Test-Path Env:PGPASSWORD) { throw 'Remove PGPASSWORD before offline validation.' }
dotnet build .\backend\NexoraDental.sln --configuration Release
dotnet test .\backend\NexoraDental.sln --configuration Release --filter "Category!=PostgreSqlIntegration&Category!=PostgreSqlConcurrency"
dotnet ef migrations has-pending-model-changes --context AppDbContext --project .\backend\DentalApi --startup-project .\backend\DentalApi
dotnet ef migrations has-pending-model-changes --context PostgreSqlAppDbContext --project .\backend\DentalApi --startup-project .\backend\DentalApi
git diff --check
```

The PowerShell helper test is
`.\backend\scripts\tests\PostgreSqlAutomation.Tests.ps1`. Run it only where
execution policy permits; do not lower or bypass policy. Live PostgreSQL
validation is separate from these commands and must be reported separately.
