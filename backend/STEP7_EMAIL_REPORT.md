# STEP 7.1 — Захиалгын имэйл мэдэгдлийн эхний хэрэгжилт

## Зорилго

Нэг амжилттай, шинээр баталгаажсан захиалгын дараа өвчтөний **баталгаажсан имэйл хаяг** руу цагийн мэдээлэл илгээх.

## Хийгдсэн зүйл

- Өмнө нь байсан `EmailService` (MailKit/SMTP)-ийг давхар ашигласан.
- `IAppointmentEmailSender` abstraction нэмсэн. Тестэд жинхэнэ SMTP ашиглах шаардлагагүй.
- Имэйлд эмнэлэг, үйлчилгээ, эмч, орон нутгийн цаг, цагийн бүс, захиалгын дугаар орно.
- **Зөвхөн шинэ 201 Created захиалгын дараа** илгээнэ. Idempotent replay (200), зөрчил (409), commit failure үед имэйл үүсгэхгүй.
- Имэйл илгээхийг `AppointmentEmail:Enabled` гэсэн **opt-in** тохиргоогоор анхнаасаа унтраасан.
- SMTP алдаа гарсан ч аль хэдийн баталгаажсан захиалгыг буцааж хүчингүй болгохгүй, API алдаатай гэж буруу мэдээлэхгүй.
- Тестэд имэйл хаяг болон бусад өгөгдөл нь `.invalid` домэйнтэй зохиомол өгөгдөл.

## Автомат тестүүд

`AppointmentApiTests` дотор:

1. Шинэ захиалга нэг имэйл хүсэлт гаргаж, ижил хүсэлтийн давталт болон conflict дахин илгээхгүй.
2. SMTP илгээх явцад алдаа гарсан ч баталгаажсан захиалга өгөгдлийн санд хэвээр үлдэх.
3. Commit failure үед имэйл хүсэлт гарахгүй.

Эдгээр тестийг энд бодитоор ажиллуулаагүй. Өөрчлөлтийг Windows laptop дээрээ дараах командаар шалгана:

```powershell
cd C:\Users\Surface\SuvdDent
dotnet build backend\NexoraDental.sln
dotnet test backend\DentalApi.IntegrationTests\DentalApi.IntegrationTests.csproj --filter FullyQualifiedName~AppointmentApiTests
dotnet test backend\NexoraDental.sln
git diff --check
```

## Бодит имэйл илгээх туршилт

Тохиргоо анхнаасаа `false` учир **одоо бодит имэйл илгээхгүй**. Тусдаа зөвшөөрөл болон хувийн SMTP тохиргоотой үед:

```powershell
$env:AppointmentEmail__Enabled = 'true'
```

Энэ тохиргоо зөвхөн тохирсон ажиллах процессод үйлчилнэ. SMTP нууц үгийг чат, Git эсвэл дэлгэцийн бичлэгт харуулахгүй.

## Хязгаарлалт / STEP 7.2

Одоогийн илгээлт **best-effort**: өгөгдлийн сангийн commit амжилттай болсон ч SMTP унтарсан/алдаатай бол имэйл дараа нь автоматаар retry хийхгүй. Мөн процесс commit-ийн дараа, имэйлээс өмнө унтарвал мэдэгдэл алдагдаж болно. Бодит хэрэглээнд найдвартай илгээх бол transactional outbox, retry/backoff, delivery log нэмэх шаардлагатай. Энэ шатанд шинэ migration, background worker, өгөгдлийн сангийн өөрчлөлт хийгээгүй.

## Өөрчлөгдөөгүй зүйл

- Одоо байгаа API request/response contract.
- Flutter UI.
- SQLite/PostgreSQL migration болон persistent school fixture өгөгдөл.
- `main` салбар, GitHub remote (commit/push хийгээгүй).
