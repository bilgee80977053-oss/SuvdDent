# Backend configuration

Set secrets via environment variables or a private secret manager. ASP.NET Core maps
double underscores (`__`) to `:` in configuration paths. Do not put credentials in
`appsettings.json` or commit a populated `appsettings.Development.json`.

| Environment variable | Purpose |
| --- | --- |
| `Database__Provider` | `Sqlite` (default) or `PostgreSQL` |
| `ConnectionStrings__Default` | Selected provider's connection string; environment only for PostgreSQL |
| `Jwt__Key` | Clinic JWT signing key (at least 32 UTF-8 bytes) |
| `Jwt__Issuer`, `Jwt__Audience` | Clinic JWT issuer and audience |
| `PlatformJwt__Key` | Separate Platform JWT signing key (at least 32 UTF-8 bytes) |
| `PlatformJwt__Issuer`, `PlatformJwt__Audience` | Separate Platform issuer/audience |
| `Email__Host`, `Email__Port` | SMTP host and port (1-65535) |
| `Email__From`, `Email__Username`, `Email__Password` | SMTP sender and credentials |
| `Seed__AdminPhone`, `Seed__AdminPassword` | Optional initial Director credentials; both required |
| `AllowedHosts` | Semicolon-separated accepted host names, for example `api.example.mn;localhost` (never `*`) |
| `Cors__AllowedOrigins__0`, `Cors__AllowedOrigins__1`, ... | Exact browser origins (scheme, host and optional port) |
| `ForwardedHeaders__Enabled` | Set to `true` only behind a trusted reverse proxy |
| `ForwardedHeaders__KnownProxies__0`, ... | Trusted proxy IP addresses |
| `ForwardedHeaders__KnownNetworks__0`, ... | Trusted proxy CIDR networks |

JWT signing keys, issuers and audiences must differ between the Clinic and Platform
schemes. Startup validates them in every environment. SMTP is required outside the
isolated `Testing` environment. The disabled Platform email/SMS sender is separate
from the legacy SMTP service; it must be replaced before enabling Platform signup.

`appsettings.Example.json` is documentation only and is not automatically loaded.
Its values are placeholders, not credentials. Replace them through private
configuration, never by committing real values into the example.

The default `AllowedHosts` is `localhost`; override it when deploying behind a
real domain. CORS denies cross-origin browsers by default until permitted origins
are configured. It never grants credentialed wildcard access.

Phase 2 migration in progress: the PostgreSQL provider package is installed and
`Database__Provider=PostgreSQL` selects it. PostgreSQL connection strings are
accepted **only** from the `ConnectionStrings__Default` environment variable,
never from JSON configuration. Step 2 adds an independent PostgreSQL baseline;
startup still intentionally rejects PostgreSQL pending a separately authorized
database rollout. SQLite also requires a configured connection string; no database
path is bundled in appsettings.json. Outside `Testing`, SQLite automatically applies
pending migrations on startup. PostgreSQL startup remains blocked before migration
or seeding. `Testing` skips application startup migration and seeding for both.

Forwarded headers are disabled by default. When enabled, configure at least one
trusted proxy IP or CIDR; unknown sources are never permitted to override the
client IP/scheme, and at most one forwarding hop is processed. Do **not** set
`ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` as a shortcut: it may trust arbitrary
forwarded values. `UseForwardedHeaders` runs before HTTPS redirection and IP-based
rate limiting. HSTS is enabled outside Development; confirm TLS and your proxy's
own HSTS behavior during deployment.

## PostgreSQL baseline (Phase 2, Step 2)

`PostgreSqlAppDbContext` inherits the shared model and owns only
`Migrations/PostgreSql`; `AppDbContext` retains the unchanged SQLite migrations
and snapshot. Runtime PostgreSQL registration resolves the dedicated context.
Always specify `--context` for EF tooling now that both contexts exist.
See [the migration workflow](Migrations/README.md) for provider-specific migration
creation, SQL generation, historical fixtures and safety precautions.

**SQLite SQL scripts must NEVER be applied to PostgreSQL.** C# migrations,
designers and each provider's snapshot are the source of truth. Archived SQLite
review SQL is not an alternative PostgreSQL baseline.

Model-only review commands (no database connection or application startup):

```powershell
dotnet ef migrations has-pending-model-changes --context PostgreSqlAppDbContext --project backend/DentalApi
dotnet ef migrations script --idempotent --context PostgreSqlAppDbContext --project backend/DentalApi
```

The design-time factories ignore application configuration and use synthetic,
credential-free connection settings. Do not run `database update` for this step.
The baseline creates a new schema; it does not upgrade an existing SQLite or
PostgreSQL database or transfer data. Its Down operation removes that schema.

PostgreSQL uses native identity integers, UUIDs, booleans and UTC timestamps.
Patient DateOfBirth remains a calendar DateTime (`timestamp without time zone`).
Availability endpoints retain UTC ticks in bigint columns, preserving comparison,
range checks and API precision. SQLite keeps its original mappings.
Platform email uses an ICU nondeterministic case-insensitive, accent-sensitive
collation (`platform_email_nocase`); PostgreSQL must support ICU. Unlike SQLite's
ASCII NOCASE, this also folds Unicode case. Existing-data collision review is
required before any future import. No extension or credentials are embedded.

Offline tests verify migration isolation, snapshot agreement, table coverage,
provider mappings and generated idempotent SQL. Execution against PostgreSQL is
reserved for a separately authorized isolated database rehearsal.


## Offline cleanup validation (Phase 2, Step 5)

Before running tests, verify that `NEXORA_PG_TEST_CONNECTION` is absent without
printing its value. With it absent, all ten real PostgreSQL tests are skipped;
only in-memory SQLite and offline tests execute. Do not configure the opt-in
connection or run real PostgreSQL tests for this cleanup. No existing database
connection, migration execution or schema change is authorized by these docs.

The archived `Migrations/Archive/Sqlite/AddPatientUserTenantIntegrity.sql` retains
its exact contents and has no test-file consumer. The scheduling, clinic location
and platform SQL scripts stay in `Migrations/` as active SQLite test fixtures.


## Docker Compose development PostgreSQL (Phase 2, Step 6)

The root [docker-compose.yml](../../docker-compose.yml) contains only PostgreSQL.
It uses `postgres:18`, a named persistent volume mounted at `/var/lib/postgresql`,
and a `pg_isready` health check. It publishes only `127.0.0.1:55432` to the
container's port 5432. The native Windows PostgreSQL instance on port 5432 and
its `nexora_dental_dev` / `nexora_dental_test` databases are separate and untouched.
Use `127.0.0.1` for the host connection; an IPv6 localhost address is not published.

| Purpose | Configuration |
| --- | --- |
| Compose database | `nexora_dental_compose_dev` |
| Compose development user | `nexora_compose_dev` |
| Compose bootstrap password | Required `NEXORA_POSTGRES_PASSWORD` in the shell environment or ignored root `.env`; no default |
| Persistent Docker volume | `nexora-dental-local_postgres_data` with the default project name |
| Future host application connection | Private `ConnectionStrings__Default`, with Docker host/port/database/user below |

The [compose.env.example](../../compose.env.example) file contains an empty
password placeholder. Copy it to root `.env` only if that file does not already
exist, then fill it privately. Both `.env` and `.env.*` remain ignored; the
example's different filename is trackable. Shell values override `.env` values.
Single-quote private dotenv values containing `$` or `#` to avoid interpolation
or comment parsing. Never print resolved Compose configuration or share container
environment output: the required password becomes part of the container environment.
This credential is for local development only. The official image's POSTGRES_USER
is a bootstrap superuser inside this isolated cluster, not a production app role.

PowerShell commands from the repository root (after manually installing Docker
Desktop with Linux containers and Compose v2):

```powershell
if (Test-Path .env) { throw 'Keep the existing .env; edit it privately.' }
Copy-Item compose.env.example .env
notepad .env
# Save a private, nonempty password before continuing.
docker compose config --quiet
docker compose up -d postgres
docker compose ps postgres
# Stop without deleting data:
docker compose stop postgres
# Optional container/network removal, retaining the named volume:
docker compose down
```

`pg_isready` reports server readiness, not password validity, application schema
presence or passing EF integration tests. Do not use volume-removal/reset flags.
Do not change the project name to work around an existing volume; inspect ownership
first. Initialization settings apply to a new empty volume only. Changing the
password variable later does not rotate an existing database user's password.

For a future explicitly authorized host application setup, build a **separate**
Npgsql connection privately with these fields:

| Field | Docker development value |
| --- | --- |
| Host | `127.0.0.1` (localhost IPv4) |
| Port | `55432` |
| Database | `nexora_dental_compose_dev` |
| Username | `nexora_compose_dev` |
| Password | Private value configured for this volume; never copy it into documentation |

Supply that connection via `ConnectionStrings__Default`, never JSON, source or
command-line arguments. Compose's `.env` is read by Compose, not automatically
by ASP.NET Core; `NEXORA_POSTGRES_PASSWORD` alone does not configure the backend.
`Database__Provider=PostgreSQL` selects the PostgreSQL context but the production
startup migration guard remains enabled. Do not disable it to run this container.
The Compose file contains no backend service, init-script mount, EF command,
application schema creation or migration runner. The official entrypoint only
bootstraps its own new PostgreSQL cluster/database. No existing Windows database
is imported, migrated or modified. SQLite SQL scripts must NEVER be applied to
PostgreSQL; any future schema rollout needs separate authorization and the
[provider migration workflow](Migrations/README.md).

The real PostgreSQL tests still require the separate, strictly validated native
`nexora_dental_test` target on port 5432. The Compose development database is not a
replacement test target. For STEP 6 build/test validation,
`NEXORA_PG_TEST_CONNECTION` must be absent and the ten real PostgreSQL tests skipped.
Do not run `psql`, EF migrations, or real database probes as validation for this task.

Docker runtime validation is **NOT TESTED** in this environment because Docker
is unavailable. Starting the image, health transitions, host connectivity and
volume persistence require a later authorized check. The `postgres:18` tag tracks
PostgreSQL 18 updates; image pinning and upgrades need review before nonlocal use.

References: [official PostgreSQL image and version 18 volume layout](https://hub.docker.com/_/postgres),
[Compose required variables and escaping](https://docs.docker.com/reference/compose-file/interpolation/).
