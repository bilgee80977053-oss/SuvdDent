# SuvdDent academic ZIP packaging

This is local/synthetic only, with no production, PostgreSQL, public hosting,
official receipt, or store signing. Ordinary Flutter releases still fail
closed without legitimate API configuration. Package only the separately
reviewed academic entry.

Run from repo root after choosing the reviewed commit:

```bash
cd mobile
flutter pub get --enforce-lockfile
flutter build web --release --no-pub --no-wasm-dry-run \
  --no-web-resources-cdn --pwa-strategy none --base-href / \
  -t lib/academic_main.dart
cd ..
dotnet publish backend/DentalApi.SchoolFixture -c Release \
  --no-self-contained -p:UseAppHost=false -o /absolute/clean/demo-fixture
python3 mobile/tool/academic_package/package-school-demo.py \
  --web mobile/build/web --fixture /absolute/clean/demo-fixture \
  --revision "$(git rev-parse HEAD)" --out /absolute/new/demo.zip
```

Use an isolated publish folder. Do not copy a database, key, credential,
personal configuration or other local files into it. Use Chrome/Edge and
run the full unpacked ZIP with .NET 10 ASP.NET Core Runtime and Python 3.8+
installed. Close the console to stop both local HTTP servers. Clean browser
storage before teacher rehearsal if reusing a prior prototype on these ports.

Before handing it over, launch the exact ZIP after extraction. Verify root page,
synthetic authentication, intended booking workflow, mobile/desktop rendering,
and no external resources. Local Linux build success is not Windows installation
or Android APK validation.
