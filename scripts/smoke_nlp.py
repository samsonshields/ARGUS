"""Exercise the real offline spaCy -> API worker -> review workflow on temporary data."""

from __future__ import annotations

import json
import os
import subprocess
import tempfile
import time

from smoke import ROOT, expect_error, free_port, request, start, stop, upload, wait_for


def job_finished(api_url: str, job_id: str, expected: str = "COMPLETED") -> dict:
    deadline = time.monotonic() + 45
    while time.monotonic() < deadline:
        job = json.loads(request(api_url + f"/api/v1/processing-jobs/{job_id}"))
        if job["state"] in {"COMPLETED", "FAILED", "CANCELLED"}:
            assert job["state"] == expected, job
            return job
        time.sleep(0.2)
    raise TimeoutError(f"Job {job_id} did not finish")


def main() -> None:
    api_port, web_port, nlp_port = free_port(), free_port(), free_port()
    api_url, web_url, nlp_url = (
        f"http://127.0.0.1:{port}" for port in (api_port, web_port, nlp_port)
    )
    python = (
        ROOT
        / "intelligence-service/.venv"
        / ("Scripts/python.exe" if os.name == "nt" else "bin/python")
    )
    if not python.exists():
        raise RuntimeError(
            "Install the intelligence-service virtual environment and English model first."
        )
    local = ROOT / ".local"
    local.mkdir(exist_ok=True)
    api = web = nlp = None
    with (
        tempfile.TemporaryDirectory(prefix="argus-nlp-smoke-") as data,
        (local / "smoke-nlp.log").open("w") as log,
    ):
        env = dict(
            os.environ,
            ARGUS_DATA_DIRECTORY=data,
            Api__BaseUrl=api_url,
            ASPNETCORE_ENVIRONMENT="Development",
            Intelligence__BaseUrl=nlp_url,
            Intelligence__WorkerEnabled="true",
            Intelligence__AutoProcessImports="true",
            Intelligence__RetryDelaySeconds="1",
            Intelligence__TimeoutSeconds="3",
            ARGUS_SPACY_MODEL="en_core_web_sm",
        )

        def launch_nlp() -> subprocess.Popen:
            return subprocess.Popen(
                [
                    str(python),
                    "-m",
                    "uvicorn",
                    "app.main:app",
                    "--host",
                    "127.0.0.1",
                    "--port",
                    str(nlp_port),
                ],
                cwd=ROOT / "intelligence-service",
                env=env,
                stdout=log,
                stderr=subprocess.STDOUT,
            )

        try:
            nlp = launch_nlp()
            wait_for(nlp_url + "/health", nlp)
            api = start("Argus.Api", api_port, env, log)
            wait_for(api_url + "/health", api)
            web = start("Argus.Web", web_port, env, log)
            wait_for(web_url, web)
            route = api_url + "/api/v1"
            inv = json.loads(
                request(
                    route + "/investigations",
                    "POST",
                    {"title": "Fictional Phase 3 training"},
                )
            )["id"]
            text = "FICTIONAL TRAINING SCENARIO ONLY. \U0001f600 Alice Morgan met a United Nations delegation in Geneva on March 12, 2026."
            original = text.encode()
            source = upload(
                route + f"/investigations/{inv}/sources",
                original,
                "fictional-ner.txt",
                "Fictional NER source",
            )["source"]["id"]
            first_job = json.loads(
                request(route + f"/processing-jobs?sourceId={source}")
            )["items"][0]
            job_finished(api_url, first_job["id"])
            mentions_url = route + f"/investigations/{inv}/mentions"
            mentions = json.loads(request(mentions_url))["items"]
            assert mentions and all(
                item["status"] == "PROPOSED" and item["entityId"] is None
                for item in mentions
            )
            person = next(item for item in mentions if item["text"] == "Alice Morgan")
            org = next(item for item in mentions if item["text"] == "United Nations")
            for mention in mentions:
                assert (
                    text.encode("utf-16-le")[
                        mention["start"] * 2 : mention["end"] * 2
                    ].decode("utf-16-le")
                    == mention["text"]
                )
                assert mention["confidence"] is None
            entity = json.loads(
                request(
                    route + f"/mentions/{person['id']}/approve",
                    "POST",
                    {"name": "Alice M. Morgan (fictional)", "type": "PERSON"},
                )
            )
            request(route + f"/mentions/{org['id']}/reject", "POST")
            evidence = json.loads(request(route + f"/mentions/{person['id']}/evidence"))
            assert (
                evidence["quote"] == "Alice Morgan"
                and "\U0001f600" in evidence["before"]
            )
            queued = json.loads(request(route + f"/sources/{source}/process", "POST"))
            job_finished(api_url, queued["id"])
            assert len(json.loads(request(mentions_url))["items"]) == len(mentions)
            assert (
                json.loads(request(mentions_url + "?status=APPROVED"))["items"][0]["id"]
                == person["id"]
            )
            assert (
                json.loads(request(mentions_url + "?status=REJECTED"))["items"][0]["id"]
                == org["id"]
            )
            assert "Alice M. Morgan" in request(
                web_url + f"/investigations/{inv}/entities"
            )
            assert "Source mentions" in request(web_url + f"/entities/{entity['id']}")
            assert "Entity review" in request(web_url + f"/sources/{source}")
            assert "Processing jobs" in request(web_url + "/processing-jobs")
            assert "/process/entities" in request(nlp_url + "/openapi.json")
            assert "/api/v1/sources/{id}/process" in request(
                api_url + "/openapi/v1.json"
            )
            assert (
                json.loads(request(route + f"/investigations/{inv}/graph"))["edges"]
                == []
            )
            expect_error(
                lambda: request(route + f"/investigations/{inv}/collaboration"), 404
            )
            stop(nlp)
            nlp = None
            offline_job = json.loads(
                request(route + f"/sources/{source}/process", "POST")
            )
            failed = job_finished(api_url, offline_job["id"], "FAILED")
            assert failed["attempts"] == 3 and failed["errorCode"] == "nlp_unavailable"
            nlp = launch_nlp()
            wait_for(nlp_url + "/health", nlp)
            retried = json.loads(
                request(route + f"/processing-jobs/{failed['id']}/retry", "POST")
            )
            job_finished(api_url, retried["id"])
            stop(api)
            env["Intelligence__WorkerEnabled"] = "false"
            api = start("Argus.Api", api_port, env, log)
            wait_for(api_url + "/health", api)
            pending = json.loads(request(route + f"/sources/{source}/process", "POST"))
            assert pending["state"] == "PENDING"
            stop(api)
            env["Intelligence__WorkerEnabled"] = "true"
            api = start("Argus.Api", api_port, env, log)
            wait_for(api_url + "/health", api)
            job_finished(api_url, pending["id"])
            assert len(json.loads(request(mentions_url))["items"]) == len(mentions)
            assert (
                json.loads(request(route + f"/entities/{entity['id']}"))["mentionCount"]
                == 1
            )
            assert (
                json.loads(request(route + f"/mentions/{person['id']}/evidence"))[
                    "textHash"
                ]
                == evidence["textHash"]
            )
            from urllib.request import urlopen

            with urlopen(
                web_url + f"/downloads/sources/{source}", timeout=10
            ) as download:
                assert download.read() == original
            print(
                "PASS: real FastAPI/spaCy + background API + Web; proposals, UTF-16 evidence, approval/rejection, deduplication, bounded offline failure/retry, durable queue restart, original preservation and no multi-user collaboration endpoints."
            )
        finally:
            stop(web)
            stop(api)
            stop(nlp)


if __name__ == "__main__":
    main()
