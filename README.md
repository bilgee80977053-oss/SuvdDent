# Nexora Dental

Монголын шүдний эмнэлгүүдэд зориулсан олон эмнэлгийн (multi-tenant) ASP.NET Core
backend. Эмнэлгийн мэдээлэл `ClinicId`-аар тусгаарлагдах бөгөөд Clinic болон
Platform хэрэглэгчийн JWT нэвтрэлт тусдаа ажилладаг.

## Одоогийн боломжууд

- Эмнэлгийн хэрэглэгч, эрхийн түвшин, BCrypt password hashing, JWT authentication
- Өвчтөний өөрийн мэдээлэл болон эмчийн профайл
- Эмчийн ажлын цаг, үйлчилгээний хугацаа, өвчтөний боломжтой цагийн суурь
- Монголын аймаг/сум, дүүрэг болон эмнэлгийн цагийн бүсийн баталгаажуулалт
- Platform account-ийн тусдаа бүртгэл/нэвтрэлтийн суурь
- SQLite and separate PostgreSQL EF Core migration histories, xUnit integration tests

**Хараахан байхгүй:** цаг захиалгын `Appointment`, эмчилгээ/төлбөр, audit log,
SMS reminder болон frontend. Эдгээрийг дараагийн үе шатуудад төлөвлөсөн.

## Төслийн бүтэц

| Зам | Үүрэг |
| --- | --- |
| `backend/NexoraDental.sln` | .NET solution |
| `backend/DentalApi/` | ASP.NET Core Web API (.NET 10) |
| `backend/DentalApi.IntegrationTests/` | xUnit integration tests, тусгаарлагдсан SQLite in-memory өгөгдөл |
| `backend/DentalApi/Migrations/` | SQLite migrations, snapshot and SQL fixtures; [workflow](backend/DentalApi/Migrations/README.md) |
| `backend/DentalApi/Migrations/PostgreSql/` | Separate PostgreSQL baseline and snapshot |

## Бэлтгэл

1. .NET 10 SDK суулгана.
2. Өөрийн **локал, туршилтын** SQLite database-ийн замыг тохируулна.
3. Шаардлагатай environment variable-уудыг private configuration/secret manager-аар өгнө.
4. Production өгөгдлийн сан ашиглахгүйгээр build/test ажиллуулна.

Нууц утга repository-д байх ёсгүй. [Тохиргооны бүрэн жагсаалт](backend/DentalApi/CONFIGURATION.md)
болон [placeholder жишээ](backend/DentalApi/appsettings.Example.json)-г үзнэ үү.

Гол environment variable-ууд:

| Нэр | Тайлбар |
| --- | --- |
| `Database__Provider` | `Sqlite` эсвэл `PostgreSQL`; одоогоор SQLite migration идэвхтэй |
| `ConnectionStrings__Default` | Сонгосон өгөгдлийн сангийн холболт (PostgreSQL үед зөвхөн environment variable) |
| `Jwt__Key`, `Jwt__Issuer`, `Jwt__Audience` | Эмнэлгийн JWT (key дор хаяж 32 UTF-8 байт) |
| `PlatformJwt__Key`, `PlatformJwt__Issuer`, `PlatformJwt__Audience` | Тусдаа Platform JWT |
| `Email__Host`, `Email__Port`, `Email__From`, `Email__Username`, `Email__Password` | SMTP тохиргоо |
| `Seed__AdminPhone`, `Seed__AdminPassword` | Шинэ local database-ийн эхний Director; заавал хосоор нь |
| `AllowedHosts` | API-г хүлээж авах host нэрс (`*` биш) |
| `Cors__AllowedOrigins__0` | Зөвшөөрөгдсөн browser origin |
| `ForwardedHeaders__Enabled`, `ForwardedHeaders__KnownProxies__0` | Итгэмжлэгдсэн reverse proxy тохиргоо |

Тестүүд бодит SMTP, production database, production token key ашигладаггүй.
`appsettings.Example.json` нь зөвхөн уншиж ашиглах жишээ бөгөөд автоматаар
ачаалагдахгүй. Signing key болон SMTP нууц үгийг тэр файлд ч бодитоор нь бүү бич.

## Build, test, run

Repository-ийн үндсэн хавтаснаас:

```bash
dotnet build backend/NexoraDental.sln
dotnet test backend/NexoraDental.sln
```

Тохиргоогоо өгч, **зөвхөн локал туршилтын өгөгдлийн сан** сонгосны дараа:

```bash
dotnet run --project backend/DentalApi
```

- `Development` орчинд API баримт бичиг `/swagger` дээр нээгдэнэ.
- `GET /health` нь нэвтрэлт шаардахгүй, database connectivity-г `Healthy` (200)
  эсвэл `Unhealthy` (503) гэж мэдээлнэ. Дотоод алдааг харуулахгүй.
- Startup үед JWT, Platform JWT болон (Testing-ээс бусад үед) Email тохиргоог
  шалгана. Эдгээр нь дутуу бол API эхлэхгүй.
- Seed тохиргоо байхгүй бол default Director password үүсгэхгүй.
- Reverse proxy forwarding нь default-оор идэвхгүй. Идэвхжүүлэхдээ зөвхөн
  итгэмжлэгдсэн IP/CIDR оруулна. HSTS нь Development-ээс бусад орчинд ажиллана.

## Local PostgreSQL with Docker Compose (Phase 2, Step 6)

The optional root `docker-compose.yml` runs only the official PostgreSQL 18 image.
Docker Desktop with Linux containers and Compose v2 must be installed and running
by the developer; this task does not install it. The container is separate from
the Windows PostgreSQL server on port 5432:

| Setting | Compose development instance |
| --- | --- |
| Windows host endpoint | `127.0.0.1:55432` (localhost port 55432) |
| Container port | `5432` |
| Database | `nexora_dental_compose_dev` |
| Development user | `nexora_compose_dev` |
| Project / named volume | `nexora-dental-local` / `nexora-dental-local_postgres_data` |
| Persistent mount | `/var/lib/postgresql` (PostgreSQL 18 layout) |

From the repository root in PowerShell, prepare private configuration:

```powershell
if (Test-Path .env) { throw 'Keep your existing .env; edit it privately instead of overwriting it.' }
Copy-Item compose.env.example .env
notepad .env
```

Fill the empty `NEXORA_POSTGRES_PASSWORD` value privately with a strong unique
local password, then save and close the editor. The example is trackable; the
actual `.env` and `.env.*` files remain ignored. Compose also accepts this variable
from the shell environment, which takes precedence over `.env`. Never print its
value or store it in source. Missing or empty passwords make Compose fail.

When Docker is available, validate without printing resolved secrets, start the
service, and check that its status becomes healthy:

```powershell
docker compose config --quiet
docker compose up -d postgres
docker compose ps postgres
```

Stop while retaining the persistent data:

```powershell
docker compose stop postgres
# Optional: remove the stopped container/network; the named volume is retained.
docker compose down
```

Do not use volume-deletion or reset options. Keep the Compose project name stable
to keep using the same named volume. Changing the initialization password in
`.env` does not rotate the password in an already initialized volume.

If the PostgreSQL client is installed, this command connects to the **Docker**
instance and prompts for the password without putting it in command history:

```powershell
psql --host=127.0.0.1 --port=55432 --username=nexora_compose_dev --dbname=nexora_dental_compose_dev --password
```

This is a connection example for a later developer session, not a STEP 6 validation
command. No database connections are part of this implementation task.

Compose initializes only its own empty PostgreSQL cluster/database on first use.
It does not start ASP.NET Core, apply EF migrations, run historical SQLite SQL,
or connect to the Windows `nexora_dental_dev` or `nexora_dental_test` databases.
Application tables must not be created automatically. The PostgreSQL application
startup migration guard remains enabled; starting the backend with PostgreSQL
still fails outside `Testing` pending a separately authorized rollout.

Use a separate private `ConnectionStrings__Default` for any future Docker-backed
application setup; never reuse the Windows database connection or change the
strict test target to point at this development instance. See
[Compose connection and safety details](backend/DentalApi/CONFIGURATION.md#docker-compose-development-postgresql-phase-2-step-6).

Docker runtime validation has **not been performed** in STEP 6 because Docker is
unavailable in the implementation environment. Image pull, startup, health and
persistence still need a later authorized runtime check.

## EF Core migrations

See the [provider-specific migration workflow](backend/DentalApi/Migrations/README.md)
for migration creation, SQL generation, source-of-truth files and safety checks.

- **SQLite:** outside `Testing`, application startup automatically applies pending
  migrations. Starting the API can change the configured SQLite schema.
- **PostgreSQL:** the startup migration guard remains enabled. Outside `Testing`,
  startup rejects PostgreSQL before running migrations or seeding.
- **Testing:** application startup skips migrations/seeding. Historical tests
  explicitly apply SQLite migrations to their isolated in-memory databases.

**SQLite SQL scripts must NEVER be applied to PostgreSQL.**

Inspect history and generate review SQL without connecting to a database:

```bash
dotnet ef migrations list --no-connect --context AppDbContext --project backend/DentalApi --startup-project backend/DentalApi
dotnet ef migrations list --no-connect --context PostgreSqlAppDbContext --project backend/DentalApi --startup-project backend/DentalApi
dotnet ef migrations script 0 20261027132306_PostgreSqlBaseline --idempotent --context PostgreSqlAppDbContext --project backend/DentalApi --startup-project backend/DentalApi
```

Always select `--context`. C# migrations, designers and each provider's snapshot
are the source of truth; SQL files are derived review/test artifacts.
`AddPatientUserTenantIntegrity.sql` is archived unchanged under
`Migrations/Archive/Sqlite/`. The other three SQLite SQL files remain in place
because historical integration tests depend on them. This cleanup changes no
C# migration, snapshot, API contract or database schema.

Design-time factories ignore application configuration: SQLite uses an in-memory
connection; PostgreSQL uses credential-free model-only settings. Generating SQL
does not authorize executing it. Do not treat `database update` with these
factories as this repository's deployment workflow. A real rollout requires
provider/range and SQL review, a consistent backup, rehearsal on an explicitly
authorized isolated copy, and separate deployment authorization.

For STEP 5 validation, verify that `NEXORA_PG_TEST_CONNECTION` is absent without
printing its value. Only in-memory SQLite and offline checks may run; all ten
real PostgreSQL tests should be skipped. See the
[integration-test instructions](backend/DentalApi.IntegrationTests/README.md).

## Аюулгүй байдлын тэмдэглэл

- API-ийн clinic scope нь хэрэглэгчийн хүсэлт дэх arbitrary `clinicId` биш,
  баталгаажсан JWT болон database ownership-оос гарна.
- Login болон шинэ seed credential-ийг log, commit, test artifact-д бүү хадгал.
- Нэвтрэх мэдээллийн өмнөх default credential-ээр нэгэнт үүссэн Director
  бүртгэл байвал seed код солигдох нь түүний password-ийг автоматаар солихгүй.
  Ийм бүртгэлийн нууц үгийг тусад нь аюулгүй аргаар солих шаардлагатай.
- Production migration-ийн өмнө tenant isolation, access control, audit болон
  backup/restore ажиллагааг дахин нягтална.

Нэмэлт API баримт бичиг:
[Doctor Profile](backend/DentalApi/DoctorProfileApi.md) ·
[Scheduling](backend/DentalApi/SchedulingApi.md) ·
[Clinic Location](backend/DentalApi/ClinicLocationApi.md) ·
[Platform Account](backend/DentalApi/PlatformAccountApi.md).
