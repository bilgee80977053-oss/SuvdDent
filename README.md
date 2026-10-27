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
