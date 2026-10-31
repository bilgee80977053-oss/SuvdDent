# STEP 5.4 — Захиалгын давталт, давхцал, зэрэгцээ хүсэлтийн шалгалт

Огноо: 2026-10-31 (Asia/Ulaanbaatar).

## Эхлэх үеийн төлөв

- Салбар: `feature/school-appointment-mvp`.
- `git status --short`: хоосон, working tree цэвэр байсан.
- Репозиторт AGENTS.md олдоогүй.
- Баталгаажуулалтын өмнө `NEXORA_PG_TEST_CONNECTION` байхгүйг утгыг нь хэвлэхгүйгээр шалгасан.

## Эх кодоос тогтоосон хамгаалалт

`AppointmentService.Create` нь идэвхтэй, баталгаажсан имэйлтэй Patient хэрэглэгч болон тухайн clinic-ийн идэвхтэй patient профайлыг transaction дотор шалгана. Clinic/user ID нь JWT claims-ээс ирдэг. `Program.cs` нь claims-ийг өгөгдлийн сангийн clinic, role, active төлөв, tokenVersion-той тулгана. Controller зөвхөн Patient role зөвшөөрнө.

RequestId нь `(ClinicId, PatientId, RequestId)` unique индексээр хамгаалагдсан. Өмнөх захиалгын doctor, service, UTC эхлэх агшин ижил бол анхны ID болон snapshot-уудыг 200-аар буцаана. Өөр мэдээлэлтэй бол 409. Шинэ захиалга 201 болон Location header буцаана.

Эмчийн buffer орсон occupied interval, өвчтөний treatment interval-ийг confirmed захиалгатай half-open нөхцөлөөр шалгана. SQLite transaction нь уншихаас өмнө write reservation авдаг; PostgreSQL нь Serializable ашигладаг. Composite foreign key нь өөр clinic-ийн patient/doctor/service холбохыг хориглоно. Энгийн индекс дангаараа хугацааны давхцлыг хориглохгүй.

SaveChanges болон commit нэг transaction-д байна. Алдаа гарвал transaction dispose/rollback болсны дараа tracker цэвэрлэж, давтагдсан хүсэлт эсэхийг нэг удаа шинээр уншина. SQLite busy/locked/unique болон PostgreSQL 23505/40001/40P01 нь аюулгүй 409 болно. Transaction автоматаар дахин гүйхгүй. Production өөрчлөлт шаардах батлагдсан доголдол илрээгүй.

Уншсан эх сурвалж: AppointmentService, AppointmentsController, Appointment model/configuration, DatabaseCompatibility, Program authentication, AppointmentApi.md, AppointmentPersistence.md; appointment API/provider/persistence/PostgreSQL tests, DoctorApiFactory, TenantIsolationTests, SchoolFixtureTests, SchoolRehearsal.ps1, SchoolFixture/Fixture.cs.

## Coverage ба өөрчлөлт

Ихэнх шаардлага өмнө нь тесттэй байсан. Шинээр ижил өргөн тестүүд давхардуулаагүй.

| Шаардлага | Ашигласан тест ба DB баталгаа |
| --- | --- |
| 1: шинэ хүсэлт 201, нэг мөр | `Valid_booking_uses_trusted_snapshots_and_UTC_and_is_readable_with_pagination`: `SingleAsync`, clinic/patient болон snapshot шалгана. |
| 2: яг ижил хүсэлт 200, анхны ID | `Idempotent_replay_survives_changed_configuration_and_payload_mismatch_conflicts`: яг ижил payload replay нэмсэн; бүтэн response болон нийт нэг мөрийг шалгана. Offset-оор ижил агшин болон тохиргоо өөрчлөгдсөн replay-г хадгалсан. |
| 3: requestId ижил, мэдээлэл өөр 409 | Дээрх тест doctor/service/start өөрчлөлт бүрийн дараа DB count=1 гэж шалгахаар бэхжүүлсэн. |
| 4: өөр requestId, эзлэгдсэн цаг 409 | Шинэ `Another_patient_cannot_book_occupied_doctor_interval_with_a_new_request_key`: өөр өвчтөн ашиглаж, эмчийн давхцлыг өвчтөний өөрийн давхцлаас тусад нь батална; DB дахь ганц мөрийн ID, эзэмшигч, request key, цагийг шалгана. Өмнөх buffer/touching boundary тестийг дахин ашигласан. |
| 5: зэрэгцээ хүсэлт | `Concurrent_independent_connections_confirm_at_most_one_booking` хоёр хувилбар: өөр өвчтөн, эсвэл duplicate request. Нэг 201, нийт нэг мөр; retry шалгана. |
| 6: rollback | `Failure_at_commit_rolls_back_insert_and_returns_safe_conflict` дөрвөн хувилбар: insert-ийн дараах commit failure үед DB count=0, дараагийн booking амжилттай. |
| 7: auth/tenant | `All_routes_require_authenticated_patient` дөрвөн хувилбарт DB count=0 нэмсэн. Existing eligibility/foreign resource тестүүд DB count=0 шалгадаг. Owner-only read/body authority болон TenantIsolationTests-ийг дахин ашигласан. |

Яг өөрчилсөн файлууд:

- `backend/DentalApi.IntegrationTests/AppointmentApiTests.cs`: нэг Fact нэмсэн, хоёр existing тестийн баталгааг бэхжүүлсэн.
- `mobile/STEP5_4_REPORT.md`: энэ тайлан болон тусдаа зөвшөөрлөөр хийх HTTP төлөвлөгөө.

Flutter code, production code/API/auth, migration/snapshot, startup guard, PostgreSQL болон local-dev.db өөрчлөөгүй. Commit/push хийгээгүй.

## Бодит validation

- Relevant run: `dotnet test backend/DentalApi.IntegrationTests/DentalApi.IntegrationTests.csproj --filter 'FullyQualifiedName~Appointment|FullyQualifiedName~SchoolFixture|FullyQualifiedName~TenantIsolation' --nologo --verbosity quiet`
- Үр дүн: **94 passed, 0 failed, 2 skipped, нийт 96**. Хоёр skipped нь opt-in PostgreSQL appointment тест.
- `dotnet build backend/NexoraDental.sln --nologo --verbosity quiet`: **амжилттай, 0 warning, 0 error**.
- `dotnet test backend/NexoraDental.sln --nologo --verbosity quiet`: **350 passed, 0 failed, 12 skipped, нийт 362**. PostgreSQL opt-in байхгүй учир live provider тестүүд алгасагдсан.
- `git diff --check`: амжилттай (exit 0); шинэ report-ийн whitespace-ийг мөн тусад нь шалгасан.

## Баталгааны хязгаар ба үлдэх эрсдэл

API тестүүд ASP.NET TestServer, real JWT validation болон тусдаа request DbContext/connection ашигласан. SQLite нь unique named shared in-memory DB, anchor connection, Pooling=False тохиргоотой. Энэ нь persistent school DB биш. SchoolFixture booking тест ч in-memory SQLite ашигласан; safety тест түр хавтсанд өөрийн plain-text sentinel үүсгээд устгадаг.

SQLite зэрэгцээ тест client талын gate-ээр хүсэлтүүдийг зэрэг сулладаг. Transaction-ууд яг зэрэг overlap predicate уншсаны баталгаа биш; scheduler нь дараалуулж болно. SQLite shared-cache locking нь file-backed WAL, filesystem locking, олон процесс, restart/crash durability-г бүрэн төлөөлөхгүй. PostgreSQL offline тестүүд connection opening-ийг хориглож Serializable сонголт, synthetic SQLSTATE mapping шалгадаг; SQLite-д оруулсан PostgreSQL exception нь live PostgreSQL rollback баталгаа биш. Live PostgreSQL race tests ажиллаагүй.

Давхцлаас хамгаалалт бүх writer ижил transaction/predicate протокол мөрдөхөөс хамаарна. Шууд SQL insert болон ирээдүйн update writer-ийг энгийн индекс хамгаалахгүй. Concurrent duplicate хүсэлт commit хараахан харагдахгүй үед 409 авч болно; completed identical retry 200 авна.

TestServer HTTP 201/200/409 шалгагдсан. Бодит TCP HTTP, Flutter LIVE, шинэ file-backed fixture дээр энэ алхамд шалгаагүй.

## Өмнөх дөрвөн захиалгатай fixture-ийн хамгаалалт

`C:\Users\Surface\AppData\Local\NexoraDental\school-test\step5-school-20261031-run2.sqlite` руу connection нээгээгүй, inspect/start/create/seed/migration/write ажиллуулаагүй. Sidecar-уудыг өөрчлөөгүй. Дөрвөн мөртэй гэдгийг хэрэглэгчийн өгсөн мэдээлэл гэж үзсэн; энэ алхамд дахин тоолоогүй. Өөрчлөгдөөгүйг hash-аар баталсан гэж мэдэгдээгүй. Шинэ persistent fixture ч үүсгээгүй, эхлүүлээгүй. Ажиллаж буй process зогсоогоогүй.

## Тусдаа зөвшөөрөл авсны дараах бодит HTTP төлөвлөгөө

**Доорх create/start/POST алхмууд одоогоор зөвшөөрөгдөөгүй, ажиллуулаагүй. Төлөвлөгөө болон сонгосон шинэ absolute path-ийг хянаж, explicit approval авсны дараа л гүйцэтгэнэ.**

1. Репогийн салбар/status-ийг дахин шалгана. PostgreSQL opt-in байвал утгыг нь хэвлэхгүйгээр зогсоно. 5018 порт эзлэгдсэн бол process автоматаар зогсоохгүй; эзэмшигчээс гараар shutdown хүснэ. Terminal transcript, HTTP body/token/password logging ашиглахгүй.
2. Repo root дахь PowerShell-д цорын ганц шинэ path сонгоно. Хуучин fixture-ийн path ашиглахгүй. Энэ хувьсагчийг дараах бүх командад хэвээр хэрэглэнэ:

```powershell
if (Test-Path Env:NEXORA_PG_TEST_CONNECTION) { throw 'PostgreSQL opt-in present; stop' }
$fixturePath = Join-Path $env:LOCALAPPDATA ('NexoraDental\school-test\step5-idempotency-' + [guid]::NewGuid().ToString('N') + '.sqlite')
.\backend\SchoolRehearsal.ps1 -Mode Plan -DatabasePath $fixturePath
```

Plan нь connection/write хийхгүй, DB болон sidecar байгаа бол татгалзана. Энэ exact target болон HTTP writes-ийн зөвшөөрөл авсны дараа:

```powershell
.\backend\SchoolRehearsal.ps1 -Mode Create -DatabasePath $fixturePath -AllowFixtureWrites
.\backend\SchoolRehearsal.ps1 -Mode Inspect -DatabasePath $fixturePath
```

3. Create-ийн secure prompt-д зөвхөн локал private password өгнө. Нууц үгийг command argument, source, report, chat-д оруулахгүй. Inspect-ийн `AppointmentCount` **0** байх ёстой; өөр бол зогсоно. `ClinicId`, `DoctorProfileId`, `ServiceId`, `SuggestedDate`-ийг энэ шинэ fixture-ийн output-оос авна, тогтмол ID таахгүй.
4. Нэг terminal-д launcher-ийг explicit write opt-in-тай ажиллуулна:

```powershell
.\backend\SchoolRehearsal.ps1 -Mode Start -DatabasePath $fixturePath -AllowFixtureWrites
```

Launcher нь exact fixture ownership, SQLite ReadWrite target, loopback `http://127.0.0.1:5018`, шинэ private JWT keys, synthetic preverified account, SMTP `127.0.0.1:9` тохируулдаг. Бодит имэйл/account ашиглахгүй; register/verify/reset/test-email ажиллуулахгүй.

5. Өөр private terminal дахь HTTP client-д `POST /api/Auth/login` хүсэлтийг `identifier=school-patient@example.invalid`, шинэ fixture-ийн clinicId, secure prompt-оор оруулсан private password-тай илгээнэ. JSON бүтэц нь `{identifier,password,clinicId}`. Token-ийг зөвхөн memory-д хадгалж Bearer header-д ашиглана; request/response бүхлээр хэвлэхгүй. HTTP client non-2xx үед шууд exception өгөхийн оронд status-ийг inspect хийх тохиргоотой байна.
6. `GET /api/appointments/me?page=1&pageSize=100`: items хоосон байхыг батална. `GET /api/appointments/services`, `GET /api/appointments/doctors?serviceId=<шинэ service ID>`-ийг шалгана. `GET /api/appointments/slots?doctorProfileId=<шинэ doctor ID>&serviceId=<шинэ service ID>&date=<SuggestedDate>`-оос дор хаяж хоёр өөр available start сонгоно. Огноо хуучирсан бол clinic-local маргаашийн огноо сонгоно.
7. Нэг GUID `R` үүсгэж `POST /api/appointments`-д `{doctorProfileId,serviceId,startUtc:эхний slot,requestId:R}` илгээнэ. **201**, Location, ID=`A` гэж шалгана. Дараа нь `/me`-д яг нэг `A` мөр байгааг шалгана.
8. Яг тэр JSON-ийг өөрчлөлтгүй дахин POST хийнэ: **200**, ID=`A`, `/me` count=**1**.
9. `R`-ийг хэвээр үлдээж startUtc-г сонгосон хоёр дахь slot болгоно: **409**, `/me` count=**1**, `A`-ийн цаг өөрчлөгдөөгүй.
10. Шинэ GUID `S` ашиглан эхний slot-д POST хийнэ: **409**, `/me` count=**1**. Шинэ GUID нь үнэхээр R-ээс өөр байхыг шалгана.
11. Operator эхлүүлсэн launcher terminal-аа Ctrl+C-ээр гараар зогсооно. Дараа нь зөвхөн шинэ path дээр `Inspect` ажиллуулна: `AppointmentCount` **1**, initial **0**-оос яг нэгээр нэмэгдсэн байх ёстой. Count/status/ID зөрвөл зогсоож, нэмэлт write/retry/reset/cleanup хийхгүй. Шинэ fixture-ийг review-д хадгална; хуучин дөрвөн захиалгатай fixture рүү ямар ч команд чиглүүлэхгүй. Password/token/header-ийг terminal memory-оос цэвэрлэнэ.

Энэ HTTP төлөвлөгөө 201/200/409 болон file-backed final count-ийг шалгана; хоёр өөр өвчтөний бодит HTTP race-г баталсан гэж үзэхгүй. Дараагийн зөв алхам нь энэ тайланг хянаж шинэ path дээрх rehearsal-ийн тусдаа зөвшөөрөл өгөх юм. Live PostgreSQL шалгалтад мөн тусдаа зөвшөөрөл шаардлагатай.
