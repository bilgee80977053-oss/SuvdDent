"""Local launcher included in the explicitly synthetic academic demo ZIP."""

import argparse
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
import os
from pathlib import Path
import shutil
import socket
import subprocess
import sys
from threading import Event, Thread
import time
from urllib.error import URLError
from urllib.request import ProxyHandler, build_opener
import webbrowser


ROOT = Path(__file__).resolve().parent
HOST = "127.0.0.1"
API_PORT = 5018
WEB_PORT = 7357
URL = f"http://{HOST}:{WEB_PORT}/"
FIXTURE = ROOT / "fixture" / "DentalApi.SchoolFixture.dll"


class LocalFiles(SimpleHTTPRequestHandler):
    extensions_map = {**SimpleHTTPRequestHandler.extensions_map, ".wasm": "application/wasm"}

    def list_directory(self, path):
        self.send_error(404)
        return None

    def end_headers(self):
        self.send_header("Cache-Control", "no-store")
        super().end_headers()


def demo_environment():
    blocked = (
        "ASPNETCORE_", "DOTNET_", "PG", "NEXORA_", "EMAIL", "JWT", "PLATFORMJWT",
        "DATABASE", "CONNECTIONSTRINGS", "URLS", "ALLOWEDHOSTS", "CORS", "FORWARDEDHEADERS",
    )
    return {
        key: value for key, value in os.environ.items()
        if key.upper() in ("DOTNET_ROOT", "DOTNET_ROOT_X64", "DOTNET_ROOT_ARM64",
                           "PATH", "HOME", "USERPROFILE", "APPDATA", "LOCALAPPDATA")
        or not key.upper().startswith(blocked)
    }


def ensure_api_port_free():
    with socket.socket() as probe:
        try:
            probe.bind((HOST, API_PORT))
        except OSError as error:
            raise RuntimeError(f"Port {API_PORT} is already in use. Stop the other local demo and retry.") from error


def relay(child, ready):
    for line in child.stdout:
        print(line, end="", flush=True)
        if line.strip() == "SUVDDENT ACADEMIC READY":
            ready.set()


def wait_for_api(child, ready):
    opener = build_opener(ProxyHandler({}))
    end = time.monotonic() + 25
    while time.monotonic() < end and child.poll() is None:
        if not ready.wait(0.15):
            continue
        try:
            with opener.open(f"http://{HOST}:{API_PORT}/health", timeout=0.7) as response:
                if response.status == 200:
                    return
        except (URLError, TimeoutError, OSError):
            time.sleep(0.15)
    raise RuntimeError("The synthetic API did not become healthy. Inspect the fixture console above.")


def run(no_browser):
    if not (ROOT / "web" / "index.html").is_file() or not FIXTURE.is_file():
        raise RuntimeError("Unzip the entire school package before starting the demo.")
    dotnet = shutil.which("dotnet")
    if not dotnet:
        raise RuntimeError("Install .NET 10 ASP.NET Core Runtime and reopen this launcher.")
    ensure_api_port_free()
    handler = partial(LocalFiles, directory=ROOT / "web")
    try:
        server = ThreadingHTTPServer((HOST, WEB_PORT), handler)
    except OSError as error:
        raise RuntimeError(f"Port {WEB_PORT} is already in use.") from error
    with server:
        child = subprocess.Popen([dotnet, str(FIXTURE), "serve-memory"], cwd=FIXTURE.parent,
                                 env=demo_environment(), stdout=subprocess.PIPE,
                                 stderr=subprocess.STDOUT, text=True, encoding="utf-8", errors="replace")
        ready = Event()
        Thread(target=relay, args=(child, ready), daemon=True).start()
        try:
            wait_for_api(child, ready)
            print(f"School demo: {URL} (synthetic data only)", flush=True)
            if not no_browser:
                webbrowser.open(URL)
            server.timeout = 0.3
            while child.poll() is None:
                server.handle_request()
            return child.returncode
        finally:
            if child.poll() is None:
                child.terminate()
                try:
                    child.wait(timeout=4)
                except subprocess.TimeoutExpired:
                    child.kill()
                    child.wait()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--no-browser", action="store_true")
    args = parser.parse_args()
    try:
        return run(args.no_browser)
    except KeyboardInterrupt:
        return 0
    except (RuntimeError, OSError) as error:
        print(f"School demo stopped: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
