"""Full fictional investigation, browser navigation, live backup and restored-workspace smoke."""

from __future__ import annotations

import hashlib
import json
import os
import subprocess
import tempfile
import time
from pathlib import Path
from urllib.parse import urlparse

from load_demo import load_demo
from playwright.sync_api import expect, sync_playwright
from smoke import ROOT, free_port, request, start, stop, wait_for


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
    local = ROOT / ".local"
    local.mkdir(exist_ok=True)
    screenshots = ROOT / "docs/screenshots"
    screenshots.mkdir(exist_ok=True)
    api = web = nlp = None
    with (
        tempfile.TemporaryDirectory(prefix="argus-demo-smoke-") as temp,
        (local / "smoke-demo.log").open("w") as log,
    ):
        workspace = Path(temp) / "workspace"
        env = dict(
            os.environ,
            ARGUS_DATA_DIRECTORY=str(workspace),
            Api__BaseUrl=api_url,
            ASPNETCORE_ENVIRONMENT="Development",
            Intelligence__BaseUrl=nlp_url,
            Intelligence__WorkerEnabled="true",
            Intelligence__AutoProcessImports="true",
            ARGUS_SPACY_MODEL="en_core_web_sm",
        )

        def call(path: str):
            return json.loads(request(api_url + "/api/v1" + path))

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
            started = time.perf_counter()
            demo = load_demo(api_url, local / "smoke-demo-result.json")
            inv = demo["investigationId"]
            graph = call(f"/investigations/{inv}/graph")
            assert len(graph["nodes"]) == 14 and len(graph["edges"]) == 18
            assert sum(edge["status"] == "PROPOSED" for edge in graph["edges"]) == 15
            assert call(f"/investigations/{inv}/events")["total"] == 10
            assert len(call(f"/investigations/{inv}/map")["events"]) == 4
            summary = call(f"/investigations/{inv}/assessments/summary")
            assert (
                summary["approved"] == 5
                and summary["approvedWithUnresolvedContradictions"] == 2
            )
            report = call(f"/reports/{demo['reportId']}")
            assert (
                report["validation"]["sourceCount"] == 12
                and "UNRESOLVED CONTRADICTION" in report["markdown"]
            )
            for merge in demo["merges"]:
                assert (
                    call(f"/entities/{merge['sourceId']}")["mergedIntoId"]
                    == merge["targetId"]
                )
            errors, external = [], []
            with sync_playwright() as playwright:
                browser = playwright.chromium.launch(
                    channel=os.environ.get("ARGUS_BROWSER_CHANNEL", "msedge"),
                    headless=True,
                )
                page = browser.new_page(viewport={"width": 1440, "height": 1200})
                page.on("pageerror", lambda error: errors.append(str(error)))

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
                page.goto(web_url + f"/investigations/{inv}")
                expect(
                    page.get_by_role(
                        "heading",
                        name="Operation Meridian — Regional Logistics and Maritime Activity",
                        exact=True,
                    )
                ).to_be_visible()
                page.screenshot(
                    path=str(screenshots / "meridian-overview.png"), full_page=True
                )
                nav = page.get_by_role(
                    "navigation", name="Investigation sections", exact=True
                )
                nav.get_by_role("link", name="Graph", exact=True).click()
                canvas = page.locator(".graph-canvas")
                expect(canvas).to_have_attribute(
                    "data-graph-ready", "true", timeout=20000
                )
                expect(canvas).to_have_attribute("data-nodes", "14")
                expect(canvas).to_have_attribute("data-edges", "18")
                graph_ms = int(canvas.get_attribute("data-render-milliseconds"))
                assert graph_ms < 5000
                page.get_by_label("Find and select node", exact=True).select_option(
                    demo["entities"]["org1"]
                )
                expect(
                    page.get_by_role(
                        "heading",
                        name="Selected node: Aster Maritime Union",
                        exact=True,
                    )
                ).to_be_visible()
                page.screenshot(
                    path=str(screenshots / "meridian-graph.png"), full_page=False
                )
                nav.get_by_role("link", name="Timeline", exact=True).click()
                expect(page.locator(".timeline li")).to_have_count(10)
                expect(page.locator(".timeline")).to_contain_text(
                    "2026-03 (month only)"
                )
                expect(page.locator(".timeline")).to_contain_text("2026 (year only)")
                page.screenshot(
                    path=str(screenshots / "meridian-timeline.png"), full_page=True
                )
                nav.get_by_role("link", name="Geography", exact=True).click()
                expect(page.locator(".map-canvas")).to_have_attribute(
                    "data-location-count", "5", timeout=20000
                )
                expect(page.locator(".map-canvas")).to_have_attribute(
                    "data-event-count", "4"
                )
                page.screenshot(
                    path=str(screenshots / "meridian-map.png"), full_page=False
                )
                nav.get_by_role("link", name="Assessments", exact=True).click()
                expect(page.locator("[data-assessment-ready]")).to_have_attribute(
                    "data-assessment-ready", "true", timeout=20000
                )
                expect(page.locator(".relationship-row")).to_have_count(5)
                page.get_by_label("Unresolved contradictions only", exact=True).check()
                page.get_by_role(
                    "button", name="Apply claim filters", exact=True
                ).click()
                expect(page.locator(".relationship-row")).to_have_count(2)
                page.screenshot(
                    path=str(screenshots / "meridian-assessments.png"), full_page=True
                )
                nav.get_by_role("link", name="Reports", exact=True).click()
                expect(page.locator("[data-report-ready]")).to_have_attribute(
                    "data-report-ready", "true", timeout=20000
                )
                page.locator(".relationship-row button").first.click()
                expect(
                    page.get_by_label("Markdown report preview", exact=True)
                ).to_contain_text("UNRESOLVED CONTRADICTION")
                page.screenshot(
                    path=str(screenshots / "meridian-report.png"), full_page=False
                )
                # Mobile navigation remains usable without horizontal document overflow.
                page.set_viewport_size({"width": 390, "height": 844})
                page.goto(web_url + f"/investigations/{inv}/assessments")
                expect(page.locator("[data-assessment-ready]")).to_have_attribute(
                    "data-assessment-ready", "true", timeout=20000
                )
                assert page.evaluate(
                    "document.documentElement.scrollWidth <= window.innerWidth + 1"
                )
                page.screenshot(path=str(local / "phase7-mobile.png"), full_page=True)
                page.set_viewport_size({"width": 1440, "height": 1200})
                page.goto(web_url + "/workspace-backup")
                page.get_by_label(
                    "I want to download the complete workspace.", exact=True
                ).check()
                with page.expect_download(timeout=120000) as download_info:
                    page.get_by_role(
                        "button", name="Download workspace backup", exact=True
                    ).click()
                download = download_info.value
                archive_path = Path(temp) / "backup.zip"
                download.save_as(archive_path)
                expect(
                    page.get_by_text(
                        "Backup downloaded. Keep it with your recovery records.",
                        exact=True,
                    )
                ).to_be_visible()
                assert not errors and not external, (errors, external)
                browser.close()
            stop(web)
            web = None
            stop(api)
            api = None
            restored_path = Path(temp) / "restored"
            configuration = env.get("ARGUS_BUILD_CONFIGURATION", "Debug")
            api_dll = ROOT / f"src/Argus.Api/bin/{configuration}/net10.0/Argus.Api.dll"
            subprocess.run(
                [
                    "dotnet",
                    str(api_dll),
                    "--restore-backup",
                    str(archive_path),
                    str(restored_path),
                ],
                cwd=ROOT,
                env=env,
                stdout=log,
                stderr=subprocess.STDOUT,
                check=True,
                timeout=120,
            )
            env["ARGUS_DATA_DIRECTORY"] = str(restored_path)
            api = start("Argus.Api", api_port, env, log)
            wait_for(api_url + "/health", api)
            web = start("Argus.Web", web_port, env, log)
            wait_for(web_url, web)
            assert (
                call(f"/reports/{demo['reportId']}")["markdown"] == report["markdown"]
            )
            assert call(f"/investigations/{inv}/claims?status=APPROVED")["total"] == 5
            assert len(call(f"/investigations/{inv}/graph")["edges"]) == 18
            for source_id in demo["sources"].values():
                metadata = call(f"/sources/{source_id}")
                content = (
                    restored_path / "documents" / (source_id.replace("-", "") + ".txt")
                ).read_bytes()
                assert hashlib.sha256(content).hexdigest() == metadata["contentHash"]
            with sync_playwright() as playwright:
                browser = playwright.chromium.launch(
                    channel=os.environ.get("ARGUS_BROWSER_CHANNEL", "msedge"),
                    headless=True,
                )
                page = browser.new_page()
                page.goto(web_url + f"/investigations/{inv}/reports")
                expect(
                    page.get_by_role("heading", name="Markdown reports", exact=True)
                ).to_be_visible()
                expect(page.locator(".relationship-row")).to_have_count(1)
                stop(api)
                api = None
                page.goto(web_url + f"/investigations/{inv}/assessments")
                expect(page.get_by_role("alert")).to_contain_text(
                    "Cannot reach the API"
                )
                expect(
                    page.get_by_role("link", name="Reload this view", exact=True)
                ).to_be_visible()
                expect(
                    page.get_by_text("Loading assessments?", exact=True)
                ).to_have_count(0)
                browser.close()
            elapsed = round(time.perf_counter() - started, 1)
            print(
                f"PASS: complete fictional Meridian workflow; 12 sources, 14 entities plus merge history, 18 edges (15 proposed/3 reviewed), 10 dated events, 5 locations, 5 claims/2 unresolved conflicts; report; desktop/mobile browser; live backup download, CLI restore, real restart and original/hash/report persistence. Graph render {graph_ms}ms; total {elapsed}s."
            )
        finally:
            stop(web)
            stop(api)
            stop(nlp)


if __name__ == "__main__":
    main()
