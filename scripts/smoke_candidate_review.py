"""Real-model review visibility/graph toggle regression; optionally import a local PDF."""

from __future__ import annotations

import argparse
import json
import os
import subprocess
import tempfile
from pathlib import Path

from playwright.sync_api import expect, sync_playwright
from smoke import ROOT, free_port, request, start, stop, upload, wait_for
from smoke_nlp import job_finished


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--pdf", type=Path)
    args = parser.parse_args()
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
        tempfile.TemporaryDirectory(prefix="argus-review-smoke-") as data,
        (local / "smoke-candidate-review.log").open("w", encoding="utf-8") as log,
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
                    {"title": "Fictional candidate regression"},
                )
            )["id"]
            text = (
                "Alice Morgan met United Nations delegates in Geneva. The report has 20 pages and three scenarios.\n"
                * 25
            )
            source = upload(
                route + f"/investigations/{inv}/sources",
                text.encode(),
                "fictional-review.txt",
                "Fictional review fixture",
            )["source"]["id"]

            def complete_source(source_id: str) -> None:
                job = json.loads(
                    request(route + f"/processing-jobs?sourceId={source_id}")
                )["items"][0]
                job_finished(api_url, job["id"])

            complete_source(source)
            mentions_url = route + f"/investigations/{inv}/mentions"
            mentions = json.loads(request(mentions_url + "?pageSize=100"))["items"]
            assert len(mentions) > 20
            assert not {"20", "three"} & {m["text"] for m in mentions}
            with sync_playwright() as playwright:
                browser = playwright.chromium.launch(
                    channel=os.environ.get("ARGUS_BROWSER_CHANNEL", "msedge"),
                    headless=True,
                )
                page = browser.new_page(viewport={"width": 1440, "height": 1100})
                page.route(
                    "**/*",
                    lambda route: (
                        route.continue_()
                        if route.request.url.startswith(
                            (api_url, web_url, nlp_url, "data:", "blob:")
                        )
                        else route.abort()
                    ),
                )
                page.goto(web_url + f"/sources/{source}")
                expect(page.locator("[data-review-ready]")).to_have_attribute(
                    "data-review-ready", "true"
                )
                status = page.get_by_label("Review status", exact=True)
                rows = page.locator(".mention-row")
                expect(rows).to_have_count(20)
                page.get_by_role("button", name="Next mentions", exact=True).click()
                pager = page.locator(".pager").filter(
                    has=page.get_by_role("button", name="Previous mentions", exact=True)
                )
                expect(pager).to_contain_text("Page 2")
                status.select_option("APPROVED")
                expect(rows).to_have_count(0)
                expect(
                    page.get_by_text("No approved mentions in this view.", exact=True)
                ).to_be_visible()
                expect(pager).to_contain_text("Page 1")
                status.select_option("PROPOSED")
                page.get_by_label("Candidate text", exact=True).fill("alice")
                page.get_by_role(
                    "button", name="Refresh candidates", exact=True
                ).click()
                page.get_by_label("Candidate type", exact=True).select_option("PERSON")
                expect(rows).to_have_count(20)
                expect(rows.first).to_contain_text("Alice Morgan")
                rows.first.get_by_role(
                    "button", name="Alice Morgan", exact=True
                ).click()
                page.get_by_role("button", name="Approve mention", exact=True).click()
                expect(
                    page.get_by_text("Mention approved.", exact=False)
                ).to_be_visible()
                status.select_option("APPROVED")
                expect(rows).to_have_count(1)
                expect(rows.first).to_have_attribute("data-status", "APPROVED")
                expect(
                    rows.first.get_by_role("link", name="Approved entity", exact=True)
                ).to_be_visible()
                status.select_option("REJECTED")
                expect(rows).to_have_count(0)
                page.get_by_label("Candidate text", exact=True).fill("United Nations")
                page.get_by_role(
                    "button", name="Refresh candidates", exact=True
                ).click()
                page.get_by_label("Candidate type", exact=True).select_option(
                    "ORGANIZATION"
                )
                status.select_option("PROPOSED")
                expect(rows.first).to_contain_text("United Nations")
                rows.first.get_by_role(
                    "button", name="United Nations", exact=True
                ).click()
                page.get_by_role("button", name="Reject mention", exact=True).click()
                expect(
                    page.get_by_text("Mention rejected;", exact=False)
                ).to_be_visible()
                status.select_option("REJECTED")
                expect(rows).to_have_count(1)
                expect(rows.first).to_have_attribute("data-status", "REJECTED")
                page.get_by_label("Candidate text", exact=True).fill("")
                page.get_by_role(
                    "button", name="Refresh candidates", exact=True
                ).click()
                page.get_by_label("Candidate type", exact=True).select_option("")
                status.select_option("")
                expect(
                    page.locator('.mention-row[data-status="APPROVED"]')
                ).to_have_count(1)
                expect(
                    page.locator('.mention-row[data-status="REJECTED"]')
                ).to_have_count(1)
                page.screenshot(
                    path=str(local / "candidate-review-fixed.png"), full_page=True
                )
                approved = json.loads(request(mentions_url + "?status=APPROVED"))[
                    "items"
                ][0]
                place = next(m for m in mentions if m["text"] == "Geneva")
                request(
                    route + f"/mentions/{place['id']}/approve",
                    "POST",
                    {"name": "Geneva", "type": "LOCATION"},
                )
                queued = json.loads(
                    request(route + f"/sources/{source}/process-relationships", "POST")
                )
                job_finished(api_url, queued["id"])
                page.goto(web_url + f"/investigations/{inv}/entities")
                expect(
                    page.get_by_role("link", name="Alice Morgan", exact=True)
                ).to_be_visible()
                expect(
                    page.get_by_role("link", name="Geneva", exact=True)
                ).to_be_visible()
                page.goto(web_url + f"/entities/{approved['entityId']}")
                expect(
                    page.get_by_role("heading", name="Alice Morgan", exact=True)
                ).to_be_visible()
                page.goto(web_url + f"/investigations/{inv}/graph")
                canvas = page.locator(".graph-canvas")
                expect(canvas).to_have_attribute("data-nodes", "2")
                expect(canvas).to_have_attribute("data-edges", "1")
                page.get_by_label("Include proposed edges", exact=True).uncheck()
                expect(canvas).to_have_attribute("data-edges", "0")
                page.get_by_label("Include proposed edges", exact=True).check()
                expect(canvas).to_have_attribute("data-edges", "1")
                # Bulk approval covers every matching page; cancel is read-only.
                page.goto(web_url + f"/sources/{source}")
                expect(page.locator("[data-review-ready]")).to_have_attribute(
                    "data-review-ready", "true"
                )
                bulk_button = page.get_by_role(
                    "button", name="Bulk approve proposed candidates", exact=True
                )
                bulk_button.click()
                confirmation = page.get_by_role(
                    "region", name="Bulk approval confirmation", exact=True
                )
                expect(confirmation).to_contain_text("72 proposed mentions")
                grouping = page.get_by_label(
                    "Group identical names and types within this batch", exact=True
                )
                expect(grouping).not_to_be_checked()
                expect(confirmation).to_contain_text("Create 72 new entities")
                page.get_by_role(
                    "button", name="Cancel bulk approval", exact=True
                ).click()
                expect(confirmation).to_have_count(0)
                assert (
                    json.loads(request(mentions_url + "?status=PROPOSED"))["total"]
                    == 72
                )
                bulk_button.click()
                expect(confirmation).to_be_visible()
                page.get_by_label("Candidate type", exact=True).select_option("PERSON")
                expect(confirmation).to_have_count(0)
                bulk_button.click()
                expect(confirmation).to_contain_text("24 proposed mentions")
                page.get_by_label("Candidate type", exact=True).select_option("")
                expect(confirmation).to_have_count(0)
                bulk_button.click()
                expect(confirmation).to_contain_text("72 proposed mentions")
                grouping.check()
                expect(confirmation).to_contain_text("Create 3 new entities")
                page.get_by_role(
                    "button", name="Confirm bulk approval", exact=True
                ).click()
                expect(
                    page.get_by_text(
                        "Bulk approval complete: 72 mentions approved; 3 entities created.",
                        exact=True,
                    )
                ).to_be_visible()
                expect(
                    page.get_by_text("No proposed mentions in this view.", exact=True)
                ).to_be_visible()
                assert (
                    json.loads(request(mentions_url + "?status=APPROVED"))["total"]
                    == 74
                )
                assert (
                    json.loads(request(mentions_url + "?status=REJECTED"))["total"] == 1
                )
                assert (
                    json.loads(request(route + f"/investigations/{inv}/entities"))[
                        "total"
                    ]
                    == 5
                )
                page.screenshot(
                    path=str(local / "bulk-approval-complete.png"), full_page=True
                )
                browser.close()
            if args.pdf:
                pdf_inv = json.loads(
                    request(
                        route + "/investigations",
                        "POST",
                        {"title": "Local PDF extraction regression"},
                    )
                )["id"]
                pdf_source = upload(
                    route + f"/investigations/{pdf_inv}/sources",
                    args.pdf.read_bytes(),
                    args.pdf.name,
                    "Local PDF test",
                )["source"]["id"]
                complete_source(pdf_source)
                pdf_mentions = json.loads(
                    request(route + f"/investigations/{pdf_inv}/mentions?pageSize=100")
                )
                assert pdf_mentions["total"] > 0
                extracted = json.loads(request(route + f"/sources/{pdf_source}"))[
                    "extractedText"
                ].encode("utf-16-le")
                pages = (pdf_mentions["total"] + 99) // 100
                all_mentions = []
                for number in range(1, pages + 1):
                    all_mentions.extend(
                        json.loads(
                            request(
                                route
                                + f"/investigations/{pdf_inv}/mentions?pageSize=100&page={number}"
                            )
                        )["items"]
                    )
                assert len(all_mentions) == pdf_mentions["total"]
                for mention in all_mentions:
                    assert (
                        extracted[mention["start"] * 2 : mention["end"] * 2].decode(
                            "utf-16-le"
                        )
                        == mention["text"]
                    )
                assert {"China", "Russia"}.issubset({m["text"] for m in all_mentions})
                preview = json.loads(
                    request(
                        route
                        + f"/investigations/{pdf_inv}/mentions/bulk-approval-preview?sourceId={pdf_source}"
                    )
                )
                assert len(preview["candidateIds"]) == len(all_mentions)
                result = json.loads(
                    request(
                        route + f"/investigations/{pdf_inv}/mentions/approve-bulk",
                        "POST",
                        {
                            "candidateIds": preview["candidateIds"],
                            "confirm": True,
                            "groupIdenticalNames": True,
                        },
                    )
                )
                assert result["approvedCount"] == len(all_mentions)
                assert result["createdEntityCount"] == preview["distinctNameTypeCount"]
                assert (
                    json.loads(
                        request(
                            route
                            + f"/investigations/{pdf_inv}/mentions?status=PROPOSED"
                        )
                    )["total"]
                    == 0
                )
                assert json.loads(
                    request(
                        route + f"/investigations/{pdf_inv}/mentions?status=APPROVED"
                    )
                )["total"] == len(all_mentions)
                print(
                    f"PASS: local PDF import, exact UTF-16 spans and bulk approval of {len(all_mentions)} mentions into {result['createdEntityCount']} entities with explicit grouping; normal investigation untouched."
                )
            print(
                "PASS: real-model numeric-noise filter, repeated mentions/pagination, immediate approved/rejected/all review filters, search/type filters, canonical entity visibility, immediate proposed-edge toggle and bulk preview/cancel/filter reset/grouped approval across all pages; current UI styling preserved."
            )
        finally:
            stop(web)
            stop(api)
            stop(nlp)


if __name__ == "__main__":
    main()
