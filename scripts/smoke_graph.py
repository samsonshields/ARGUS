"""Real API/NLP graph workflow plus headless Edge interaction/performance verification."""

from __future__ import annotations

import json
import os
import subprocess
import tempfile
import time
from urllib.parse import urlparse

from playwright.sync_api import expect, sync_playwright
from smoke import ROOT, expect_error, free_port, request, start, stop, upload, wait_for
from smoke_nlp import job_finished


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
    api = web = nlp = None
    with (
        tempfile.TemporaryDirectory(prefix="argus-graph-smoke-") as data,
        (local / "smoke-graph.log").open("w") as log,
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
            ARGUS_SPACY_MODEL="en_core_web_sm",
        )
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
            route = api_url + "/api/v1"
            inv = json.loads(
                request(
                    route + "/investigations",
                    "POST",
                    {"title": "FICTIONAL graph performance training"},
                )
            )["id"]
            # Every identity below is an explicitly corrected fictional training identity.
            # All data is written through normal API imports/review, never direct Python DB writes.
            pairs = [
                (node, (node + offset) % 100)
                for node in range(100)
                for offset in (1, 2, 3)
            ]
            text = "Alice Morgan met Bob Carter.\n" * len(pairs)
            source = upload(
                route + f"/investigations/{inv}/sources",
                text.encode(),
                "fictional-graph-training.txt",
                "FICTIONAL 100-node training source",
            )["source"]["id"]
            ner_job = json.loads(
                request(route + f"/processing-jobs?sourceId={source}")
            )["items"][0]
            job_finished(api_url, ner_job["id"])
            mentions = []
            for number in range(1, 7):
                mentions.extend(
                    json.loads(
                        request(
                            route
                            + f"/investigations/{inv}/mentions?page={number}&pageSize=100"
                        )
                    )["items"]
                )
            mentions.sort(key=lambda item: item["start"])
            assert len(mentions) == 600, len(mentions)
            entities = {}
            for index, mention in enumerate(mentions):
                identity = pairs[index // 2][index % 2]
                name = f"Fictional Node {identity:03d}" + (
                    " <img src=x onerror=alert(1)>" if identity == 0 else ""
                )
                entity_type = "ORGANIZATION" if identity == 0 else "PERSON"
                body = {
                    "name": name,
                    "type": entity_type,
                    "entityId": entities.get(identity),
                }
                approved = json.loads(
                    request(route + f"/mentions/{mention['id']}/approve", "POST", body)
                )
                entities[identity] = approved["id"]
            assert len(entities) == 100
            generation = json.loads(
                request(route + f"/investigations/{inv}/relationships/generate", "POST")
            )
            assert len(generation["jobs"]) == 1
            assert generation["jobs"][0]["kind"] == "RELATIONSHIPS"
            job_finished(api_url, generation["jobs"][0]["id"])
            graph_url = route + f"/investigations/{inv}/graph"
            graph = json.loads(request(graph_url))
            assert len(graph["nodes"]) == 100 and len(graph["edges"]) == 300
            assert graph["componentCount"] == 1
            assert all(node["degree"] == 6 for node in graph["nodes"])
            assert all(edge["status"] == "PROPOSED" for edge in graph["edges"])
            first_edge, second_edge = graph["edges"][0], graph["edges"][1]
            proof = json.loads(request(route + f"/relationships/{first_edge['id']}"))
            assert "Alice Morgan met Bob Carter." in proof["evidence"][0]["quote"]
            with sync_playwright() as playwright:
                browser = playwright.chromium.launch(
                    channel=os.getenv("ARGUS_BROWSER_CHANNEL", "msedge"), headless=True
                )
                page = browser.new_page(viewport={"width": 1440, "height": 1100})
                errors, dialogs = [], []
                page.on("pageerror", lambda error: errors.append(str(error)))
                page.on(
                    "dialog",
                    lambda dialog: (dialogs.append(dialog.message), dialog.dismiss()),
                )
                # Rendering must work without any external browser/CDN request.
                page.route(
                    "**/*",
                    lambda intercepted: (
                        intercepted.continue_()
                        if urlparse(intercepted.request.url).hostname
                        in {"localhost", "127.0.0.1"}
                        else intercepted.abort()
                    ),
                )
                before = time.perf_counter()
                page.goto(web_url + f"/investigations/{inv}/graph")
                canvas = page.locator(".graph-canvas")
                expect(canvas).to_have_attribute(
                    "data-graph-ready", "true", timeout=20000
                )
                expect(canvas).to_have_attribute("data-nodes", "100")
                expect(canvas).to_have_attribute("data-edges", "300")
                render_ms = int(canvas.get_attribute("data-render-milliseconds"))
                assert render_ms < 5000, render_ms
                load_ms = round((time.perf_counter() - before) * 1000)
                page.screenshot(
                    path=str(local / "phase4-graph-100-300.png"), full_page=True
                )
                initial_zoom = float(canvas.get_attribute("data-zoom"))
                page.get_by_role("button", name="Zoom in", exact=True).click()
                page.wait_for_function(
                    "initial => Number(document.querySelector('.graph-canvas').dataset.zoom) > initial",
                    arg=initial_zoom,
                )
                zoom = float(canvas.get_attribute("data-zoom"))
                page.get_by_role("button", name="Zoom out", exact=True).click()
                page.wait_for_function(
                    "prior => Number(document.querySelector('.graph-canvas').dataset.zoom) < prior",
                    arg=zoom,
                )
                box = canvas.bounding_box()
                pan = canvas.get_attribute("data-pan")
                page.mouse.move(box["x"] + 8, box["y"] + 8)
                page.mouse.down()
                page.mouse.move(box["x"] + 100, box["y"] + 80, steps=10)
                page.mouse.up()
                assert canvas.get_attribute("data-pan") != pan
                page.get_by_label("Find and select node", exact=True).select_option(
                    entities[1]
                )
                expect(
                    page.get_by_role(
                        "heading", name="Selected node: Fictional Node 001"
                    )
                ).to_be_visible()
                page.get_by_label("Find and select node", exact=True).select_option("")
                expect(
                    page.get_by_role(
                        "heading", name="Selected node: Fictional Node 001"
                    )
                ).not_to_be_visible()
                canvas.scroll_into_view_if_needed()
                center = canvas.bounding_box()
                canvas.click(
                    position={"x": center["width"] / 2, "y": center["height"] / 2}
                )
                expect(
                    page.get_by_role(
                        "heading", name="Selected node: Fictional Node 001"
                    )
                ).to_be_visible()
                page.get_by_role(
                    "button", name="Focus selected node", exact=True
                ).click()
                expect(canvas).to_have_attribute("data-visible-nodes", "1")
                page.get_by_role("button", name="Expand neighbors", exact=True).click()
                expect(canvas).to_have_attribute("data-visible-nodes", "7")
                page.get_by_role("button", name="Show full graph", exact=True).click()
                expect(canvas).to_have_attribute("data-visible-nodes", "100")
                page.get_by_label("Path start", exact=True).select_option(entities[1])
                page.get_by_label("Path end", exact=True).select_option(entities[8])
                page.get_by_role(
                    "button", name="Highlight shortest path", exact=True
                ).click()
                expect(page.get_by_text("Path found:", exact=False)).to_be_visible()
                assert int(canvas.get_attribute("data-path-nodes")) > 1
                page.get_by_label("Entity type", exact=True).select_option(
                    "ORGANIZATION"
                )
                page.get_by_role(
                    "button", name="Apply graph filters", exact=True
                ).click()
                expect(canvas).to_have_attribute("data-nodes", "1")
                expect(canvas).to_have_attribute("data-edges", "0")
                page.get_by_label("Entity type", exact=True).select_option("")
                page.get_by_role(
                    "button", name="Apply graph filters", exact=True
                ).click()
                expect(canvas).to_have_attribute("data-edges", "300")
                page.get_by_label("Include proposed edges", exact=True).uncheck()
                page.get_by_role(
                    "button", name="Apply graph filters", exact=True
                ).click()
                expect(canvas).to_have_attribute("data-edges", "0")
                page.get_by_label("Include proposed edges", exact=True).check()
                page.get_by_role(
                    "button", name="Apply graph filters", exact=True
                ).click()
                expect(canvas).to_have_attribute("data-edges", "300")
                # View operations must not alter any relationship or review state.
                assert json.loads(request(graph_url))["edges"] == graph["edges"]
                page.get_by_label(
                    "Select edge for citations", exact=True
                ).select_option(first_edge["id"])
                expect(
                    page.get_by_role(
                        "heading", name="Relationship evidence", exact=True
                    )
                ).to_be_visible()
                expect(page.locator("blockquote")).to_contain_text(
                    "Alice Morgan met Bob Carter."
                )
                page.get_by_label("Include this passage in review", exact=True).check()
                page.get_by_label(
                    "I reviewed the selected passages;", exact=False
                ).check()
                page.get_by_role(
                    "button", name="Approve selected evidence", exact=True
                ).click()
                expect(
                    page.get_by_text("Relationship evidence approved.", exact=False)
                ).to_be_visible()
                approved = json.loads(
                    request(route + f"/relationships/{first_edge['id']}")
                )
                assert approved["relationship"]["status"] == "APPROVED"
                assert approved["relationship"]["type"] == "MENTIONED_WITH"
                page.get_by_label(
                    "Select edge for citations", exact=True
                ).select_option(second_edge["id"])
                page.get_by_label("Include this passage in review", exact=True).check()
                page.get_by_label(
                    "Reviewed relationship type", exact=True
                ).select_option("ASSOCIATED_WITH")
                page.get_by_label(
                    "Analyst reasoning (required for substantive relationships)",
                    exact=True,
                ).fill(
                    "Fictional training assertion explicitly reviewed by the analyst."
                )
                page.get_by_label(
                    "I reviewed the selected passages;", exact=False
                ).check()
                page.get_by_role(
                    "button", name="Approve selected evidence", exact=True
                ).click()
                expect(
                    page.get_by_text("Relationship evidence approved.", exact=False)
                ).to_be_visible()
                page.get_by_label("Include proposed edges", exact=True).uncheck()
                page.get_by_role(
                    "button", name="Apply graph filters", exact=True
                ).click()
                expect(canvas).to_have_attribute("data-edges", "2")
                page.screenshot(path=str(local / "phase4-graph.png"), full_page=True)
                assert not errors, errors
                assert not dialogs, dialogs
                browser.close()
            # Reprocess using the real sentence service: no duplicates or decision resets.
            again = json.loads(
                request(route + f"/sources/{source}/process-relationships", "POST")
            )
            job_finished(api_url, again["id"])
            assert len(json.loads(request(graph_url))["edges"]) == 300
            stop(api)
            api = start("Argus.Api", api_port, env, log)
            wait_for(api_url + "/health", api)
            assert (
                len(json.loads(request(graph_url + "?includeProposed=false"))["edges"])
                == 2
            )
            preserved = json.loads(
                request(route + f"/relationships/{first_edge['id']}")
            )
            assert preserved["evidence"][0]["quote"] == proof["evidence"][0]["quote"]
            expect_error(
                lambda: request(route + f"/investigations/{inv}/collaboration"), 404
            )
            print(
                f"PASS: real 100-node/300-edge graph; Edge render {render_ms}ms, initial load {load_ms}ms; pan/zoom, filters, neighbors, path, evidence review, offline assets, XSS-safe labels, stable reprocessing and restart persistence. Screenshot: .local/phase4-graph.png"
            )
        finally:
            stop(web)
            stop(api)
            stop(nlp)


if __name__ == "__main__":
    main()
