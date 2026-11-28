"""Start and exercise the standalone classroom runtime. Synthetic loopback only."""
import datetime as dt
import json
from pathlib import Path
import queue
import re
import signal
import subprocess
import sys
import threading
import time
import urllib.error
import urllib.request
import uuid

origin = "http://127.0.0.1:5018"
web = "http://localhost:7357"
http = urllib.request.build_opener(urllib.request.ProxyHandler({}))
events = queue.Queue()
dll = Path(sys.argv[1]).resolve(strict=True)
if dll.name != "DentalApi.SchoolFixture.dll":
    raise SystemExit("Pass the standalone SchoolFixture DLL.")


def request(method, path, expected=200, bearer=None, body=None, headers=None, wait_for_auth_window=False):
    req = urllib.request.Request(origin + path, data=None if body is None else json.dumps(body).encode(),
                                 headers={"Content-Type": "application/json", **(headers or {})},
                                 method=method)
    if bearer:
        req.add_header("Authorization", "Bearer " + bearer)
    try:
        response = http.open(req, timeout=12)
    except urllib.error.HTTPError as error:
        response = error
    if response.status == 429 and wait_for_auth_window:
        response.close()
        # The real auth limiter is intentionally preserved in academic mode.
        time.sleep(61)
        return request(method, path, expected, bearer, body, headers)
    with response:
        assert response.status == expected, f"{method} {path}: {response.status} != {expected}"
        text = response.read().decode()
        return response.headers, json.loads(text) if "application/json" in response.headers.get("Content-Type", "") else text


def wait_for(pattern):
    while True:
        line = events.get(timeout=30)
        if line is None:
            raise AssertionError("The isolated classroom host exited before the expected event.")
        match = re.search(pattern, line)
        if match:
            return match.group(1)


def login(email, password, role):
    _, payload = request("POST", "/api/Auth/login", body={
        "identifier": email, "clinicId": 1, "password": password
    }, wait_for_auth_window=True)
    assert payload["role"] == role
    return payload["token"]


flags = subprocess.CREATE_NEW_PROCESS_GROUP if sys.platform == "win32" else 0
with subprocess.Popen(["dotnet", str(dll), "serve-memory"], cwd=dll.parent, stdout=subprocess.PIPE,
                      stderr=subprocess.STDOUT, text=True, encoding="utf-8", creationflags=flags,
                      start_new_session=sys.platform != "win32") as server:
    def read():
        try:
            for line in server.stdout:
                events.put(line)
        finally:
            events.put(None)

    threading.Thread(target=read, daemon=True).start()
    try:
        password = wait_for(r"This run's synthetic password: (Demo-[A-F0-9]+!)")
        request("GET", "/health")
        cors, _ = request("OPTIONS", "/api/Auth/login", 204, headers={
            "Origin": web, "Access-Control-Request-Method": "POST",
            "Access-Control-Request-Headers": "content-type"
        })
        assert cors.get("Access-Control-Allow-Origin") == web
        cors_denied, _ = request("OPTIONS", "/api/Auth/login", 204, headers={
            "Origin": "https://example.invalid", "Access-Control-Request-Method": "POST"
        })
        assert cors_denied.get("Access-Control-Allow-Origin") is None

        patient = login("school-patient@example.invalid", password, "Patient")
        request("GET", "/api/Patients/me", bearer=patient)
        _, catalog = request("GET", "/api/appointments/services", bearer=patient)
        assert sorted(item["minPrice"] for item in catalog) == [15000, 35000, 50000]
        service = catalog[0]["id"]
        _, doctors = request("GET", f"/api/appointments/doctors?serviceId={service}", bearer=patient)
        doctor = doctors[0]["id"]
        local_day = (dt.datetime.now(dt.timezone(dt.timedelta(hours=8))).date() + dt.timedelta(days=1)).isoformat()
        _, available = request("GET", f"/api/appointments/slots?doctorProfileId={doctor}&serviceId={service}&date={local_day}",
                               bearer=patient)
        old_start = available["slots"][0]["startUtc"]
        _, booking = request("POST", "/api/appointments", 201, patient, {
            "doctorProfileId": doctor, "serviceId": service, "startUtc": old_start, "requestId": str(uuid.uuid4())
        })
        _, change = request("GET", f"/api/appointments/{booking['id']}/reschedule-slots?date={local_day}",
                            bearer=patient)
        new_start = next(s["startUtc"] for s in change["slots"] if s["startUtc"] != old_start)
        _, replacement = request("POST", f"/api/appointments/{booking['id']}/reschedule", 200, patient,
                                 {"startUtc": new_start, "requestId": str(uuid.uuid4())})
        assert replacement["id"] != booking["id"]
        _, cancelled = request("POST", f"/api/appointments/{replacement['id']}/cancel", 200, patient)
        assert cancelled["status"] == "Cancelled"
        _, history = request("GET", "/api/appointments/me", bearer=patient)
        assert {booking["id"], replacement["id"]}.issubset({item["id"] for item in history["items"]})
        request("GET", f"/api/appointments/{booking['id']}/invoice-draft-review", 404, patient)

        director = login("school-director@example.invalid", password, "Director")
        request("GET", "/api/admin/services", bearer=director)
        request("GET", "/api/admin/services", 403, patient)
        clinician = login("school-doctor@example.invalid", password, "Doctor")
        today = dt.datetime.now(dt.timezone.utc).date()
        _, assigned = request("GET", "/api/clinical/appointments/assigned"
                              f"?from={today-dt.timedelta(days=2)}&until={today+dt.timedelta(days=1)}",
                              bearer=clinician)
        assert assigned["items"]

        email = "new-school-patient@example.invalid"
        _, registration = request("POST", "/api/Auth/register", body={
            "clinicId": 1, "fullName": "Synthetic new student", "email": email, "password": password
        })
        code = wait_for(r"Classroom verification code: ([0-9]{6})")
        request("POST", "/api/Auth/verify", body={"userId": registration["userId"], "code": code})
        login(email, password, "Patient")

        print("Academic loopback: CORS, profile, catalog, doctors, booking, reschedule, cancellation, "
              "history, registration/verification, Director and Doctor authorization passed.")
    finally:
        if server.poll() is None:
            server.send_signal(signal.CTRL_BREAK_EVENT if sys.platform == "win32" else signal.SIGINT)
            try:
                server.wait(timeout=10)
            except subprocess.TimeoutExpired:
                server.kill()
                server.wait(timeout=5)
