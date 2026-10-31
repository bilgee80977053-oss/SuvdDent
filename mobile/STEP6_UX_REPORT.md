# SCHOOL MVP — STEP 6: Flutter UI/UX сайжруулалт

Огноо: 2026-10-31 (Asia/Ulaanbaatar).

## Эхлэх үеийн төлөв, хамрах хүрээ

- Салбар: `feature/school-appointment-mvp`.
- Эхлэх үеийн `git status --short`: хоосон; working tree цэвэр.
- `mobile/README.md`, DEMO/LIVE дэлгэцүүд, Material theme, shared widget-үүд, model/controller болон existing widget/API mock тестүүдийг уншсан.
- DEMO нь `app/`, `features/`, `data/` бүтэцтэй; LIVE нь `live/` дотор API client, controller, typed model, presentation-аа тусгаарласан. Одоо байгаа бүтцийг үргэлжлүүлэн ашигласан.
- Зөвхөн Flutter UI, холбогдох тест болон энэ тайланг өөрчилсөн. Backend, API contract, auth security, idempotency/concurrency, migration, SQLite/PostgreSQL болон өмнөх rehearsal fixture-д хүрээгүй. Бодит API/өгөгдлийн сантай холбогдоогүй. Commit/push хийгээгүй.

## Өмнө ба дараа

| Асуудал | Сайжруулсан байдал |
| --- | --- |
| Түүх нээгдэхэд “Имэйл баталгаажуулалт”, “Үйлчилгээ сонгох” зэрэг booking гарчиг, алхмын progress харагддаг байсан. | Түүх нь “Миний захиалгууд” гэсэн ганц үндсэн гарчигтай, booking алхмын гарчиг/progress/сонголтын хураангуйгүй тусдаа харагдана. |
| Түүхээс toolbar/system Back дарахад booking-ийн алхмыг хойшлуулдаг байсан. | Back нь эхлээд түүхийг хааж, өмнөх booking алхам болон сонголтыг хадгална. Дэлгэцийн буцах товч мөн энэ зарчмаар ажиллана. |
| Нэвтэрсний дараах профайл руу орох дэлгэц имэйл баталгаажуулах гарчигтай байсан. | Нэвтэрсэн session-тэй үед “Нэвтрэлт амжилттай”; registration verification дэлгэц хуучин зориулалтаараа үлдсэн. |
| Захиалгын карт зөвхөн төлөв, дугаар, нийлмэл эхлэх/дуусах тексттэй байсан. | Дугаар, төлөв, эмч, үйлчилгээ, жилтэй огноо, эхлэх цаг, дуусах цагийг тусад нь гаргана. Карт хооронд зай, мэдээлэл хооронд separator, тодорхой typography ашигласан. |
| Сонголтын картууд хооронд зай бага, сонгогдсон төлөв сул байсан. | Карт хооронд 12 px зай, дотор padding, сонгосон өнгөт дэвсгэр, radio тэмдэглэгээ нэмсэн. |
| Олон ижил өнгийн үйлдлийн товч, дараагийн алхмын тайлбар дутмаг байсан. | Алхам бүрд дараагийн үйлдлийн тайлбар, сонгосон үйлчилгээ/эмчийн хураангуй нэмсэн. Профайл/үйлчилгээ/эмч/түүх шинэчлэх туслах товчийг үндсэн үйлдлээс өнгөөр ялгасан. |
| Сонгосон цаг нь interval болон duration гэсэн нийлмэл тексттэй байсан. | Огноо, эхлэх цаг, үргэлжлэх хугацаа, хүлээгдэж буй дуусах цагийг тусдаа шошготой картаар slot болон confirmation дэлгэцэд харуулна. |
| Жижиг дэлгэц дээр padding/toolbar том байсан. | 400 px-ээс нарийн дэлгэцэд үндсэн padding 16 px; бусад үед 24 px. Brand toolbar текстийг 17 px болгосон. 760 px content max-width хадгалсан. Картын эхлэх/дуусах цаг нарийн үед босоо, өргөн үед зэрэгцээ байрлана. |
| DEMO confirmation-д transaction/commit гэх техникийн үг байсан. | “Энэ нь зөвхөн сургалтын жишээ. Захиалга хадгалахгүй, имэйл илгээхгүй.” гэсэн ойлгомжтой тайлбартай. |

Material 3, Nexora-ийн teal өнгө, цагаан карт, canvas өнгө, үндсэн typography-г хадгалсан. Input талбаруудад нэгдсэн хүрээ, дэвсгэр, padding нэмсэн. LIVE action товч 48 px-ээс намсахгүй; existing slot touch target болон scroll бүтэц хадгалагдсан.

## Мэдээллийн үнэн зөв байдал ба цагийн бүс

`Appointment` хариунд doctorProfileId/serviceId байдаг боловч эмч/үйлчилгээний нэр байдаггүй. UI нь зөвхөн тухайн session-д серверээс өмнө нь авсан catalog-ийн ID яг таарсан нэрийг ашиглана. Түүх нээхдээ шинэ catalog endpoint дуудахгүй, дурын ID хайхгүй. Нэр байхгүй/хоосон бол:

- “Эмчийн нэр ирээгүй. Эмнэлгээс лавлана уу.”
- “Үйлчилгээний нэр ирээгүй. Эмнэлгээс лавлана уу.”

Техникийн doctor/service ID-г харуулахгүй. Захиалгын дугаарыг `№…` гэсэн өвчтөнд зориулсан лавлах дугаар хэвээр харуулна. Exact synthetic school doctor/service placeholder-уудын Монгол шошго, туршилтын тайлбар хадгалагдсан. DEMO-ийн зохиомол өгөгдлийг бодит мэт танилцуулахгүй.

Огноо, цаг нь тухайн appointment-ийн `clinicTimeZoneId` эсвэл slot response-ийн `timeZoneId`-аар existing IANA timezone conversion ашиглана. Цаг HH:mm байна. Улаанбаатар, Ховдыг Монгол нэрээр харуулна. End нь дараагийн өдөр байвал огноог нь мөн харуулна. Танихгүй timezone-той history нь таамаглахын оронд UTC гэдгийг тодорхой сануулж форматласан цаг үзүүлнэ. UTC request instant, slot ordering, clinic-local date picker-ийн хугацаа, server rules өөрчлөгдөөгүй.

## Яг өөрчилсөн файлууд

1. `mobile/lib/app/app_theme.dart` — existing Material theme-ийн input загварыг нэгтгэсэн.
2. `mobile/lib/live/live_patient_flow.dart` — history navigation/title, алхмын заавар, сонголтын feedback/summary, responsive padding, booking/time/history cards, туслах товчны өнгө.
3. `mobile/lib/live/live_presentation.dart` — `AppointmentDetailsCard`, `BookingTimeSummary`, Монгол timezone шошго.
4. `mobile/lib/features/booking/booking_confirmation_screen.dart` — DEMO-ийн техникийн тайлбарыг Монгол ойлгомжтой өгүүлбэрээр сольсон.
5. `mobile/test/live_api_test.dart` — existing booking flow тестийн нийлмэл summary текстийн assertion-ийг шинэ тусдаа duration тексттэй тааруулсан; тестийг устгаагүй. Шинэ тестүүд эхлэх/дуусах цагийг тусад нь шалгана.
6. `mobile/test/ux_refinement_test.dart` — 9 шинэ widget regression test.
7. `mobile/STEP6_UX_REPORT.md` — энэ тайлан.

Dependency, controller, API client, DTO өөрчлөгдөөгүй.

## Нэмсэн тестүүд

Бүгд synthetic mock response ашиглана; сүлжээ, бодит token/account, SMTP, өгөгдлийн сан ашиглахгүй.

- 2 тест: DEMO-ийн бүх алхам, completion-ийг 320 болон 1280 px-д явуулж layout exception гараагүйг шалгасан; бодит захиалга/имэйл үүсээгүй гэсэн тайлбарыг баталсан.
- 3 тест: 320/390/1280 px-д login-оос түүх нээх, зөв гарчиг, booking progress алга байх, картын шаардлагатай мэдээлэл, нэргүй fallback, жилтэй Монгол огноо, HH:mm; toolbar Back, system Back, дэлгэцийн буцах товч өмнөх алхам/сонголтыг хадгалахыг шалгасан.
- 1 тест: server-ийн page/pageSize-ийг ашигласан next/previous pagination, эхний хуудасны disabled previous төлөв, 1→2→1 request дараалал.
- 2 тест: 320/1280 px-д 1.6 дахин томруулсан тексттэй appointment card, урт нэр, сонгосон огноо, duration, эхлэх/дуусах цаг харагдах ба layout exception гарахгүй байх.
- 1 тест: танихгүй timezone-ийн UTC сануулга болон танихгүй status-ийн Монгол fallback; raw status утгыг дэлгэцэд гаргахгүй байх.

Existing DEMO controller, patient journey, LIVE API/auth/session reset, uncertainty/retry/conflict, timezone/date picker/slot order болон synthetic label тестүүд хадгалагдсан.

## Бодит validation

| Команд | Үр дүн |
| --- | --- |
| Focused `flutter test test/ux_refinement_test.dart test/live_api_test.dart test/live_presentation_test.dart` | Эхний UI өөрчлөлтийн дараа 58 passed, 0 failed. Үүний дараа DEMO responsive 2 тест болон form/button polish нэмсэн. |
| `flutter analyze` (`mobile/`) | Амжилттай, No issues found. |
| `flutter test` (`mobile/`) | **65 passed, 0 failed, 0 skipped**; бүх тест амжилттай. |
| `flutter build web` (`mobile/`) | Амжилттай, `Built build/web`; Wasm dry run мөн амжилттай. CupertinoIcons font reference-ийн build warning гарсан; build алдаагүй. |
| `git diff --check` | Амжилттай, exit 0. Шинэ файл дахь trailing whitespace болон дуусаагүй placeholder-уудыг мөн шалгасан. |

Dart formatter ашигласан. Web build нь аппыг сервертэй холбож ажиллуулахгүй. Backend startup болон database команд ажиллуулаагүй.

## Үлдсэн хязгаарлалт

- Appointment API нэр буцаадаггүй тул history-г login-ийн дараа шууд нээхэд нэрийн fallback хэвийн харагдана. Catalog нэр нь одоогийн нэр; захиалга үүсэх үеийн нэрийн snapshot гэж тайлбарлахгүй. Үүнийг UI дотор зохиомлоор нөхөөгүй.
- Pagination нь өмнөх contract-оор item count/pageSize дээр үндэслэнэ; нийт хуудас/нийт захиалгын тоо зохиохгүй. Бүтэн сүүлийн хуудасны дараа хоосон дараагийн хуудас харагдаж болно.
- Responsive баталгаа нь дээрх хэмжээтэй widget tests болон web compilation. Бодит Android төхөөрөмж, mobile keyboard, browser rendering, screen reader болон гар ажиллагаатай харагдах байдлын review энэ алхамд хийгдээгүй.
- Бодит backend-тэй LIVE end-to-end booking, имэйл хүргэлт, persistence, concurrency-г энэ UI алхам дахин шалгаагүй. Амжилт зөвхөн API-ийн existing 201/200 response-оос гардаг хэвээр; mock тестийг бодит booking гэж үзэхгүй.
- Үл мэдэгдэх timezone үед UTC сануулгатай fallback-ийг хадгалсан; буруу орон нутгийн цаг таахгүй.

Review-д бэлэн эх код болон тайлан үлдээв. Commit/push хийгээгүй.
