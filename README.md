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
- SQLite + EF Core migrations, xUnit integration tests

**Хараахан байхгүй:** цаг захиалгын `Appointment`, эмчилгээ/төлбөр, audit log,
SMS reminder болон frontend. Эдгээрийг дараагийн үе шатуудад төлөвлөсөн.

## Төслийн бүтэц

| Зам | Үүрэг |
| --- | --- |
| `backend/NexoraDental.sln` | .NET solution |
| `backend/DentalApi/` | ASP.NET Core Web API (.NET 10) |
| `backend/DentalApi.IntegrationTests/` | xUnit integration tests, тусгаарлагдсан SQLite in-memory өгөгдөл |
| `backend/DentalApi/Migrations/` | EF Core migration-ийн үндсэн C# түүх болон SQL review artifacts |

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
| `ConnectionStrings__Default` | Зөвшөөрөгдсөн SQLite холболт |
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
dotnet build backend/DentalApi.IntegrationTests/DentalApi.IntegrationTests.csproj -c Release
dotnet test backend/DentalApi.IntegrationTests/DentalApi.IntegrationTests.csproj -c Release
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

## EF Core migrations — маш чухал

`Program.cs` одоогоор **Testing-ээс бусад орчинд startup хийхдээ**
`db.Database.Migrate()` ажиллуулдаг. Энэ нь өгөгдлийн сангийн schema-г өөрчлөх
учраас production DB рүү API-г шууд чиглүүлж асааж болохгүй.

Аюулгүй дараалал:

1. Migration C# файл, SQL review artifact, schema өөрчлөлтийг review хийнэ.
2. Өгөгдлийн сангийн transaction-safe backup авна. SQLite WAL горимд байгаа
   database-г ажиллаж байхад энгийн file copy-оор backup хийхээс зайлсхий;
   SQLite-ийн backup механизм эсвэл бичилтийг зогсоосон copy хэрэглэнэ.
3. Backup-аас тусгаарлагдсан staging copy сэргээнэ.
4. Шинэ migration-ийг **зөвхөн тэр staging copy** дээр startup-аар ажиллуулж,
   хүснэгтийн бүтэц, row count, foreign keys, data integrity-г шалгана.
5. Production deployment болон downtime-г тусад нь зөвшөөрсний дараа л
   live migration хийх шийдвэр гаргана.

Model болон migration түүхийг харахад `dotnet-ef` CLI ашиглаж болно:

```bash
dotnet ef migrations list --project backend/DentalApi --startup-project backend/DentalApi
dotnet ef migrations script --project backend/DentalApi --startup-project backend/DentalApi
```

Энэ repository-ийн `AppDbContextDesignTimeFactory` **in-memory SQLite** ашигладаг.
Тиймээс `dotnet ef database update`-ийг live database-д migration хийх арга
гэж энд үзэхгүй. Миграцийг production-д хэрэглэх процесс нь backup, staging
rehearsal, зөвшөөрөл шаардана. Хуучин EF migration-уудыг засах/устгахгүй;
шинэ schema өөрчлөлт бүр шинэ migration байх ёстой.

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
