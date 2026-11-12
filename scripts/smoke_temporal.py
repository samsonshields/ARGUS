"""Real date pipeline, API persistence and Edge timeline/Leaflet interaction on temporary data."""

from __future__ import annotations

import json
import os
import subprocess
import tempfile
from urllib.parse import urlparse

from playwright.sync_api import expect, sync_playwright
from smoke import ROOT, expect_error, free_port, request, start, stop, upload, wait_for
from smoke_nlp import job_finished


def main():
    api_port, web_port, nlp_port = free_port(), free_port(), free_port()
    api_url, web_url, nlp_url = (
        f"http://127.0.0.1:{port}" for port in (api_port, web_port, nlp_port)
    )
    python = (
        ROOT
        / "intelligence-service/.venv"
        / ("Scripts/python.exe" if os.name == "nt" else "bin/python")
    )
    local = ROOT / ".local"
    local.mkdir(exist_ok=True)
    api = web = nlp = None
    with (
        tempfile.TemporaryDirectory(prefix="argus-temporal-smoke-") as data,
        (local / "smoke-temporal.log").open("w") as log,
    ):
        env = dict(
            os.environ,
            ARGUS_DATA_DIRECTORY=data,
            Api__BaseUrl=api_url,
            ASPNETCORE_ENVIRONMENT="Development",
            Intelligence__BaseUrl=nlp_url,
            Intelligence__WorkerEnabled="true",
            Intelligence__AutoProcessImports="true",
            ARGUS_SPACY_MODEL="en_core_web_sm",
        )
        route = api_url + "/api/v1"

        def call(path, method="GET", body=None):
            result = request(route + path, method, body)
            return json.loads(result) if result else None

        try:
            nlp = subprocess.Popen(
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
            wait_for(nlp_url + "/health", nlp)
            api = start("Argus.Api", api_port, env, log)
            wait_for(api_url + "/health", api)
            web = start("Argus.Web", web_port, env, log)
            wait_for(web_url, web)
            inv = call(
                "/investigations",
                "POST",
                {"title": "FICTIONAL temporal and geographic training"},
            )["id"]
            text = "\U0001f600 Fictional training: Alice Morgan met Bob Carter in London on 2026-02-10. In March 2026 they returned. Last Tuesday another visit was reported. The ambiguous date 03/04/2026 remains unresolved."
            source = upload(
                route + f"/investigations/{inv}/sources",
                text.encode(),
                "fictional-temporal.txt",
                "FICTIONAL dated source",
            )["source"]["id"]
            job_finished(
                api_url, call(f"/processing-jobs?sourceId={source}")["items"][0]["id"]
            )
            mentions = call(f"/investigations/{inv}/mentions?pageSize=100")["items"]
            london = next(item for item in mentions if item["text"] == "London")
            entity = call(
                f"/mentions/{london['id']}/approve",
                "POST",
                {
                    "name": "Fictional Port Meridian <img src=x onerror=alert(1)>",
                    "type": "LOCATION",
                },
            )
            job = call(f"/sources/{source}/process-events", "POST")
            assert job["kind"] == "EVENTS"
            job_finished(api_url, job["id"])
            events = call(f"/investigations/{inv}/events?status=PROPOSED")["items"]
            assert len(events) == 4, events
            assert [item["datePrecision"] for item in events] == [
                "EXACT",
                "MONTH",
                "UNKNOWN",
                "UNKNOWN",
            ]
            item = events[0]
            assert item["evidence"][0]["quote"].startswith("\U0001f600")
            passage = call(f"/sources/{source}/passages")["items"][0]
            location = call(
                f"/investigations/{inv}/locations",
                "POST",
                {
                    "entityId": entity["id"],
                    "precision": "APPROXIMATE",
                    "latitude": 35,
                    "longitude": 30,
                    "uncertaintyMeters": 25000,
                    "basis": "EXPLICIT_SELECTION",
                    "confirmCoordinates": True,
                    "notes": "Explicit fictional training center; 25 km uncertain region, not a precise facility.",
                },
            )
            call(
                f"/locations/{location['id']}/evidence",
                "POST",
                {
                    "snapshotId": passage["snapshotId"],
                    "start": passage["start"],
                    "end": passage["end"],
                    "revision": location["revision"],
                },
            )
            location = call(f"/locations/{location['id']}")
            call(
                f"/locations/{location['id']}/approve",
                "POST",
                {"confirm": True, "revision": location["revision"]},
            )
            browser_errors = []
            external = []
            with sync_playwright() as playwright:
                browser = playwright.chromium.launch(
                    channel=os.environ.get("ARGUS_BROWSER_CHANNEL", "msedge"),
                    headless=True,
                )
                page = browser.new_page(viewport={"width": 1440, "height": 1100})
                page.on("pageerror", lambda error: browser_errors.append(str(error)))

                def offline(intercepted):
                    if urlparse(intercepted.request.url).hostname in {
                        "127.0.0.1",
                        "localhost",
                    }:
                        intercepted.continue_()
                    else:
                        external.append(intercepted.request.url)
                        intercepted.abort()

                page.route("**/*", offline)
                page.goto(web_url + f"/investigations/{inv}/timeline")
                page.get_by_label("Event review status", exact=True).select_option(
                    "PROPOSED"
                )
                page.get_by_role("button", name="Apply filters", exact=True).click()
                page.get_by_role(
                    "button", name="Date mention: 2026-02-10", exact=True
                ).click()
                inspector = page.locator('[aria-label="Event inspector"]')
                expect(inspector.get_by_text("Original date mention:")).to_be_visible()
                page.get_by_label("Title", exact=True).fill("Fictional port meeting")
                page.get_by_label("Type", exact=True).select_option("MEETING")
                page.get_by_label("Approved location", exact=True).select_option(
                    location["id"]
                )
                page.get_by_label("Find event entities", exact=True).fill("Meridian")
                page.get_by_role("button", name="Find entities", exact=True).click()
                page.get_by_label(entity["name"] + " (LOCATION)", exact=True).check()
                page.get_by_role(
                    "button", name="Save event proposal", exact=True
                ).click()
                expect(
                    page.get_by_text(
                        "Event saved as proposed. Review its citations before approval."
                    )
                ).to_be_visible()
                page.get_by_label(
                    "I reviewed this event and its evidence; confirm the selected action.",
                    exact=True,
                ).check()
                page.get_by_role("button", name="Approve event", exact=True).click()
                expect(
                    page.get_by_text("Event action completed: approve.")
                ).to_be_visible()
                assert call(f"/events/{item['id']}")["status"] == "APPROVED"
                page.get_by_label("Event review status", exact=True).select_option(
                    "APPROVED"
                )
                page.get_by_role("button", name="Apply filters", exact=True).click()
                expect(page.locator(".timeline")).to_contain_text("2026-02-10")
                page.get_by_label("Date precision", exact=True).select_option("MONTH")
                page.get_by_label("Date value", exact=True).fill("2026-04")
                page.get_by_role(
                    "button", name="Save event proposal", exact=True
                ).click()
                expect(
                    page.get_by_text(
                        "Event saved as proposed. Review its citations before approval."
                    )
                ).to_be_visible()
                page.get_by_label(
                    "I reviewed this event and its evidence; confirm the selected action.",
                    exact=True,
                ).check()
                page.get_by_role("button", name="Approve event", exact=True).click()
                expect(page.locator(".timeline")).to_contain_text(
                    "2026-04 (month only)"
                )
                page.screenshot(path=str(local / "phase5-timeline.png"), full_page=True)
                assert not external, external
                page.goto(web_url + f"/investigations/{inv}/map")
                canvas = page.locator(".map-canvas")
                expect(canvas).to_have_attribute(
                    "data-location-count", "1", timeout=20000
                )
                expect(canvas).to_have_attribute("data-event-count", "1")
                assert page.locator(".leaflet-overlay-pane path").count() >= 3
                assert not external, external
                page.get_by_label("Choose mapped record", exact=True).select_option(
                    "event:" + item["id"]
                )
                expect(
                    page.get_by_role(
                        "heading", name="Fictional port meeting", exact=True
                    )
                ).to_be_visible()
                expect(
                    page.get_by_role("link", name="Open event and review on timeline")
                ).to_be_visible()
                page.get_by_label("Map event type", exact=True).select_option(
                    "MOVEMENT"
                )
                page.get_by_role("button", name="Apply map filters", exact=True).click()
                expect(canvas).to_have_attribute("data-event-count", "0")
                page.get_by_label("Map event type", exact=True).select_option("")
                page.get_by_role("button", name="Apply map filters", exact=True).click()
                expect(canvas).to_have_attribute("data-event-count", "1")
                page.get_by_label("Choose mapped record", exact=True).select_option(
                    "location:" + location["id"]
                )
                expect(
                    page.locator('[aria-label="Location inspector"]')
                ).to_be_visible()
                before = call(f"/locations/{location['id']}")
                canvas.click(position={"x": 350, "y": 150})
                expect(
                    page.get_by_text(
                        "Coordinates copied into draft. Choose precision and confirm before saving."
                    )
                ).to_be_visible()
                assert (
                    call(f"/locations/{location['id']}")["revision"]
                    == before["revision"]
                )
                page.get_by_label(
                    "I explicitly selected these coordinates or verified them against the cited source metadata.",
                    exact=True,
                ).check()
                page.get_by_role(
                    "button", name="Save location proposal", exact=True
                ).click()
                expect(canvas).to_have_attribute("data-location-count", "0")
                page.get_by_label(
                    "I reviewed the location, uncertainty and citations; confirm the selected action.",
                    exact=True,
                ).check()
                page.get_by_role("button", name="Approve location", exact=True).click()
                expect(canvas).to_have_attribute("data-location-count", "1")
                expect(canvas).to_have_attribute("data-event-count", "1")
                page.get_by_role(
                    "button", name="Save current geographic focus", exact=True
                ).click()
                expect(
                    page.get_by_text("Geographic focus saved.", exact=True)
                ).to_be_visible()
                assert call(f"/investigations/{inv}/map")["focus"] is not None
                # Actual SVG marker click opens the event and its preserved source citation.
                page.locator(".leaflet-overlay-pane path").last.click(force=True)
                expect(
                    page.get_by_role(
                        "heading", name="Fictional port meeting", exact=True
                    )
                ).to_be_visible()
                assert page.locator('img[src="x"]').count() == 0
                page.screenshot(path=str(local / "phase5-map.png"), full_page=True)
                page.get_by_role(
                    "button", name="Load OpenStreetMap basemap (online)", exact=True
                ).click()
                expect(
                    page.get_by_text(
                        "Online tiles are unavailable. The coordinate grid and reviewed markers remain usable.",
                        exact=True,
                    )
                ).to_be_visible()
                expect(page.locator(".leaflet-control-attribution")).to_contain_text(
                    "OpenStreetMap contributors"
                )
                assert external and all(
                    url.startswith("https://tile.openstreetmap.org/")
                    for url in external
                )
                page.get_by_role(
                    "button", name="Use offline coordinate grid", exact=True
                ).click()
                assert not browser_errors, browser_errors
                browser.close()
            call(f"/sources/{source}/process-events", "POST")
            jobs = call(f"/processing-jobs?sourceId={source}")["items"]
            job_finished(api_url, jobs[0]["id"])
            assert (
                len(call(f"/investigations/{inv}/events?allStatuses=true")["items"])
                == 4
            )
            assert call(f"/events/{item['id']}")["title"] == "Fictional port meeting"
            stop(api)
            api = start("Argus.Api", api_port, env, log)
            wait_for(api_url + "/health", api)
            persisted = call(f"/investigations/{inv}/map")
            assert (
                len(persisted["events"]) == 1
                and len(persisted["locations"]) == 1
                and persisted["focus"]
            )
            assert call(f"/events/{item['id']}")["datePrecision"] == "MONTH"
            expect_error(
                lambda: request(route + f"/investigations/{inv}/collaboration"), 404
            )
            print(
                "PASS: real date pipeline, exact/partial/relative/ambiguous dates, reviewed timeline updates, entity/source links, offline Leaflet markers/circles/selection/filtering, draft-only map clicks, location review, focus, optional tile failure/attribution, XSS-safe labels, deduplication and API restart persistence; no multi-user endpoints."
            )
        finally:
            stop(web)
            stop(api)
            stop(nlp)


if __name__ == "__main__":
    main()
