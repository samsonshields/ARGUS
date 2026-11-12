"""Launch built API/Web, exercise HTTP routes and verify persistence after restart."""

from __future__ import annotations

import hashlib
import json
import os
import socket
import subprocess
import tempfile
import time
from collections.abc import Callable
from pathlib import Path
from urllib.error import HTTPError
from urllib.request import Request, urlopen

ROOT = Path(__file__).resolve().parents[1]


def free_port() -> int:
    with socket.socket() as connection:
        connection.bind(("127.0.0.1", 0))
        return connection.getsockname()[1]


def request(url: str, method: str = "GET", body: dict | None = None) -> str:
    data = json.dumps(body).encode() if body is not None else None
    with urlopen(
        Request(
            url, data=data, method=method, headers={"Content-Type": "application/json"}
        ),
        timeout=60,
    ) as response:
        return response.read().decode()


def upload(url: str, content: bytes, filename: str, title: str) -> dict:
    boundary = "argus-smoke-multipart-boundary"
    body = bytearray()
    for key, value in {"Title": title, "PublishedAt": "2026-01-15"}.items():
        body.extend(
            f'--{boundary}\r\nContent-Disposition: form-data; name="{key}"\r\n\r\n{value}\r\n'.encode()
        )
    body.extend(
        f'--{boundary}\r\nContent-Disposition: form-data; name="File"; filename="{filename}"\r\nContent-Type: application/octet-stream\r\n\r\n'.encode()
    )
    body.extend(content)
    body.extend(f"\r\n--{boundary}--\r\n".encode())
    with urlopen(
        Request(
            url,
            data=bytes(body),
            headers={"Content-Type": f"multipart/form-data; boundary={boundary}"},
        ),
        timeout=60,
    ) as response:
        return json.loads(response.read())


def expect_error(action: Callable[[], object], status: int) -> None:
    try:
        action()
    except HTTPError as error:
        assert error.code == status, (error.code, error.read())
    else:
        raise AssertionError(f"Expected HTTP {status}")


def wait_for(url: str, process: subprocess.Popen) -> None:
    deadline = time.monotonic() + 30
    while time.monotonic() < deadline:
        if process.poll() is not None:
            raise RuntimeError(f"Server exited with {process.returncode}")
        try:
            request(url)
            return
        except OSError:
            time.sleep(0.25)
    raise TimeoutError(url)


def start(project: str, port: int, env: dict, log) -> subprocess.Popen:
    directory = ROOT / "src" / project
    configuration = env.get("ARGUS_BUILD_CONFIGURATION", "Debug")
    return subprocess.Popen(
        [
            "dotnet",
            str(directory / "bin" / configuration / "net10.0" / f"{project}.dll"),
            "--urls",
            f"http://127.0.0.1:{port}",
        ],
        cwd=directory,
        env=env,
        stdout=log,
        stderr=subprocess.STDOUT,
    )


def stop(process: subprocess.Popen | None) -> None:
    if process is not None:
        process.terminate()
        process.wait(timeout=15)


def main() -> None:
    api_port, web_port = free_port(), free_port()
    api_url, web_url = f"http://127.0.0.1:{api_port}", f"http://127.0.0.1:{web_port}"
    local = ROOT / ".local"
    local.mkdir(exist_ok=True)
    api = web = None
    with (
        tempfile.TemporaryDirectory(prefix="argus-smoke-") as data,
        (local / "smoke.log").open("w") as log,
    ):
        env = dict(
            os.environ,
            ARGUS_DATA_DIRECTORY=data,
            Api__BaseUrl=api_url,
            ASPNETCORE_ENVIRONMENT="Development",
            Intelligence__WorkerEnabled="false",
            Intelligence__AutoProcessImports="false",
        )
        try:
            api = start("Argus.Api", api_port, env, log)
            wait_for(api_url + "/health", api)
            route = api_url + "/api/v1/investigations"
            created = json.loads(
                request(
                    route,
                    "POST",
                    {
                        "title": "Fictional Meridian smoke test",
                        "description": "Synthetic research only",
                        "status": "Active",
                        "region": "Atlantic",
                    },
                )
            )
            identifier = created["id"]
            web = start("Argus.Web", web_port, env, log)
            wait_for(web_url, web)
            assert "Fictional Meridian smoke test" in request(web_url)
            assert "Fictional Meridian smoke test" in request(
                web_url + "/investigations"
            )
            assert "Fictional Meridian smoke test" in request(
                web_url + f"/investigations/{identifier}"
            )
            assert "New investigation" in request(web_url + "/investigations/new")
            assert "Edit investigation" in request(
                web_url + f"/investigations/{identifier}/edit"
            )
            assert "color-scheme:dark" in request(web_url + "/app.css")
            request(
                route + "/" + identifier,
                "PUT",
                {
                    "title": "Fictional Meridian updated",
                    "description": "Edited scope",
                    "status": "Archived",
                },
            )
            sources_url = route + f"/{identifier}/sources"
            original = (
                ROOT / "sample-data/documents/meridian-training-report.txt"
            ).read_bytes() + b'\n<script>alert("encoded")</script>'
            text_source = upload(
                sources_url, original, "training.txt", "Fictional training text"
            )
            source_id = text_source["source"]["id"]
            assert text_source["source"]["processingStatus"] == "Ready"
            assert text_source["contentHash"] == hashlib.sha256(original).hexdigest()
            pdf = (
                ROOT / "sample-data/documents/meridian-training-report.pdf"
            ).read_bytes()
            pdf_source = upload(
                sources_url, pdf, "training.pdf", "Fictional training PDF"
            )
            assert pdf_source["source"]["processingStatus"] == "Ready"
            assert "Aster Maritime" in pdf_source["extractedText"]
            expect_error(
                lambda: upload(sources_url, original, "renamed.txt", "Duplicate"), 409
            )
            expect_error(
                lambda: upload(
                    sources_url, b"binary", "unsupported.exe", "Unsupported"
                ),
                415,
            )
            failed = upload(
                sources_url,
                b"%PDF-1.7 malformed",
                "broken.pdf",
                "Malformed training PDF",
            )
            assert failed["source"]["processingStatus"] == "Failed"
            request(
                sources_url + "/text",
                "POST",
                {
                    "title": "Pasted training note",
                    "text": "Fictional pasted text " + "\u03a9" * 40000,
                },
            )
            assert (
                len(
                    json.loads(
                        request(
                            sources_url + "?search=cargo&from=2026-01-01&to=2026-01-31"
                        )
                    )
                )
                == 1
            )
            assert "Fictional training text" in request(
                web_url + f"/investigations/{identifier}/sources"
            )
            rendered = request(web_url + f"/sources/{source_id}")
            assert (
                "&lt;script&gt;" in rendered
                and '<script>alert("encoded")</script>' not in rendered
            )
            with urlopen(
                web_url + f"/downloads/sources/{source_id}", timeout=10
            ) as download:
                assert download.read() == original
                assert download.headers["Content-Disposition"].startswith("attachment")
            request(api_url + f"/api/v1/sources/{source_id}/extract-text", "POST")
            stop(api)
            api = start("Argus.Api", api_port, env, log)
            wait_for(api_url + "/health", api)
            persisted = json.loads(request(route + "/" + identifier))
            assert persisted["title"] == "Fictional Meridian updated"
            assert persisted["status"] == "Archived"
            assert "Fictional Meridian updated" in request(
                web_url + f"/investigations/{identifier}"
            )
            assert len(json.loads(request(sources_url))) == 4
            assert (
                json.loads(request(api_url + f"/api/v1/sources/{source_id}"))[
                    "contentHash"
                ]
                == hashlib.sha256(original).hexdigest()
            )
            with urlopen(
                api_url + f"/api/v1/sources/{source_id}/original", timeout=10
            ) as download:
                assert download.read() == original
            expect_error(
                lambda: request(route + "/" + identifier + "?confirm=true", "DELETE"),
                409,
            )
            disposable = json.loads(
                request(route, "POST", {"title": "Disposable empty investigation"})
            )["id"]
            request(route + f"/{disposable}?confirm=true", "DELETE")
            assert len(json.loads(request(route))) == 1
            print(
                "PASS: API/Web launch and pages; text/PDF/paste import, hash, duplicates, safe failures, search, encoded viewer, original downloads, extraction retry, CRUD restrictions and restart persistence."
            )
        finally:
            stop(web)
            stop(api)


if __name__ == "__main__":
    main()
