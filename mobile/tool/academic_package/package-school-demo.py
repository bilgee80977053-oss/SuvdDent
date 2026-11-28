"""Zip only prebuilt web files, the fixture publish, and local launchers.

Build the reviewed academic entry separately. This script never publishes, starts,
or invokes an external service. A package is not verified until launched after unzip.
"""

import argparse
import hashlib
import json
from pathlib import Path
import re
from zipfile import ZIP_DEFLATED, ZipFile


TOOL = Path(__file__).resolve().parent
PREFIX = "SuvdDent_Academic_Demo"
README = """SuvdDent | Багшид үзүүлэх сургалтын хувилбар

1. ZIP файлыг бүрэн задал.
2. Windows дээр START_WINDOWS.cmd файлыг ажиллуул.
   Linux/macOS: python3 run-school-demo.py
3. Хөтөч http://127.0.0.1:7357/ хаягаар нээгдэнэ.
4. Дуусмагц ажиллуулсан цонхонд Ctrl+C дар.

Шаардлага: Python 3.8+, .NET 10 ASP.NET Core Runtime, Chrome/Edge.
Энэ нь зөвхөн компьютер дээр ажиллах сургалтын жишээ.
Өвчтөн, эмнэлэг, цаг захиалга нь зохиомол. Төлбөр хийхгүй.
Демо зогсоход тухайн ээлжийн мэдээлэл устна. Интернэтэд нийтлэхгүй.
"""


def require(condition, message):
    if not condition:
        raise SystemExit(message)


def add_tree(zip_file, source, output_path):
    for file in sorted(source.rglob("*")):
        require(not file.is_symlink(), f"Linked package file rejected: {file.name}")
        if file.is_file():
            relative = file.relative_to(source)
            # Flutter emits an internal cache marker at the web root; it is not a runtime asset.
            if output_path == "web" and relative.as_posix() == ".last_build_id":
                continue
            require(not any(part.startswith(".") for part in relative.parts),
                    f"Hidden package file rejected: {file.name}")
            require(file.suffix.lower() not in {".sqlite", ".db", ".pfx", ".pem", ".key"},
                    f"Data or private key file rejected: {file.name}")
            zip_file.write(file, f"{PREFIX}/{output_path}/{relative.as_posix()}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--web", type=Path, required=True)
    parser.add_argument("--fixture", type=Path, required=True)
    parser.add_argument("--revision", required=True)
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    require(re.fullmatch(r"[0-9a-f]{40}", args.revision), "Supply the full source commit SHA.")
    require(args.out.suffix.lower() == ".zip" and not args.out.exists(), "Use a new .zip output.")
    for file in ("index.html", "main.dart.js", "flutter_bootstrap.js", "canvaskit/canvaskit.wasm"):
        require((args.web / file).is_file(), f"Web artifact is missing {file}.")
    require(re.search(r'"useLocalCanvasKit"\s*:\s*true',
                      (args.web / "flutter_bootstrap.js").read_text(encoding="utf-8")),
            "Build web with --no-web-resources-cdn before packaging.")
    require(b"http://127.0.0.1:5018" in (args.web / "main.dart.js").read_bytes(),
            "Academic loopback API was not found. Build the dedicated academic entry.")
    require(all((args.fixture / f"DentalApi.SchoolFixture.{extension}").is_file()
                for extension in ("dll", "deps.json", "runtimeconfig.json")), "Published fixture is missing.")
    require(args.out.parent.is_dir(), "ZIP parent directory must exist.")

    with ZipFile(args.out, "x", ZIP_DEFLATED, compresslevel=6) as file:
        file.writestr(f"{PREFIX}/README_MN.txt", README)
        file.writestr(f"{PREFIX}/BUILD-METADATA.json", json.dumps({
            "revision": args.revision,
            "purpose": "synthetic academic demo, fixture on loopback only",
        }, indent=2) + "\n")
        for name in ("run-school-demo.py", "START_WINDOWS.cmd"):
            file.write(TOOL / name, f"{PREFIX}/{name}")
        add_tree(file, args.web, "web")
        add_tree(file, args.fixture, "fixture")
    with ZipFile(args.out) as file:
        require(file.testzip() is None, "ZIP integrity check failed.")
    print(f"SHA-256 {hashlib.sha256(args.out.read_bytes()).hexdigest()}  {args.out.name}")


if __name__ == "__main__":
    main()
