"""Phase 6 real API/Web, claim review and CommonMark export verification on temporary data."""

from __future__ import annotations

import hashlib
import json
import os
import tempfile
from urllib.parse import urlparse

from markdown_it import MarkdownIt
from playwright.sync_api import expect, sync_playwright
from smoke import ROOT, expect_error, free_port, request, start, stop, wait_for


def main() -> None:
    api_port, web_port = free_port(), free_port()
    api_url, web_url = f"http://127.0.0.1:{api_port}", f"http://127.0.0.1:{web_port}"
    route = api_url + "/api/v1"
    local = ROOT / ".local"
    local.mkdir(exist_ok=True)
    api = web = None
    with (
        tempfile.TemporaryDirectory(prefix="argus-assessment-smoke-") as data,
        (local / "smoke-assessments.log").open("w") as log,
    ):
        env = dict(
            os.environ,
            ARGUS_DATA_DIRECTORY=data,
            Api__BaseUrl=api_url,
            ASPNETCORE_ENVIRONMENT="Development",
            Intelligence__WorkerEnabled="false",
            Intelligence__AutoProcessImports="false",
        )

        def call(path, method="GET", body=None):
            result = request(route + path, method, body)
            return json.loads(result) if result else None

        try:
            api = start("Argus.Api", api_port, env, log)
            wait_for(api_url + "/health", api)
            web = start("Argus.Web", web_port, env, log)
            wait_for(web_url, web)
            inv = call(
                "/investigations",
                "POST",
                {
                    "title": "FICTIONAL assessment and report training",
                    "description": "Compare conflicting statements about a fabricated meeting date.",
                },
            )["id"]
            sources = []
            for title, text, reliability in [
                (
                    "Fictional supporting release",
                    "\U0001f600 Fictional training meeting occurred on 2026-02-10. <script>alert(1)</script> [bad](javascript:alert(1))",
                    "A",
                ),
                (
                    "Fictional contradictory report",
                    "The fictional meeting instead occurred on 2026-02-11.",
                    "D",
                ),
                (
                    "Fictional context note",
                    "The fabricated organization records meetings using local dates.",
                    "C",
                ),
            ]:
                sources.append(
                    call(
                        f"/investigations/{inv}/sources/text",
                        "POST",
                        {"title": title, "text": text, "reliability": reliability},
                    )["source"]["id"]
                )
            errors, external = [], []
            with sync_playwright() as playwright:
                browser = playwright.chromium.launch(
                    channel=os.environ.get("ARGUS_BROWSER_CHANNEL", "msedge"),
                    headless=True,
                )
                page = browser.new_page(viewport={"width": 1440, "height": 1100})
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
                page.goto(web_url + f"/investigations/{inv}/assessments")
                expect(page.locator("[data-assessment-ready]")).to_have_attribute(
                    "data-assessment-ready", "true", timeout=20000
                )
                page.get_by_role("button", name="Create claim", exact=True).click()
                statement = "The fictional meeting date remains disputed."
                page.get_by_label("Claim statement", exact=True).fill(statement)
                page.get_by_label("Analyst confidence", exact=True).select_option(
                    "MODERATE"
                )
                page.get_by_label(
                    "Analyst interpretation and reasoning", exact=True
                ).fill(
                    "Both reports address the fictional meeting but give different dates. This is analyst interpretation; the discrepancy remains unresolved."
                )
                page.get_by_role(
                    "button", name="Save claim proposal", exact=True
                ).click()
                expect(
                    page.get_by_text("Claim saved as proposed.", exact=True)
                ).to_be_visible()
                claim_id = call(f"/investigations/{inv}/claims")["items"][0]["id"]
                page.get_by_text("Add or classify a source passage", exact=True).click()
                for source, kind in zip(
                    sources, ["SUPPORTS", "CONTRADICTS", "CONTEXT"], strict=True
                ):
                    page.get_by_label(
                        "Evidence relationship", exact=True
                    ).select_option(kind)
                    page.get_by_label("Analyst evidence note", exact=True).fill(
                        "Analyst compared this exact preserved passage."
                    )
                    page.get_by_label(
                        "Claim citation source", exact=True
                    ).select_option(source)
                    page.get_by_role(
                        "button", name="Attach passage", exact=False
                    ).click()
                    expect(
                        page.get_by_text(
                            "Evidence saved. Review the claim again before approval.",
                            exact=True,
                        )
                    ).to_be_visible()
                    expect(
                        page.locator("figure.citation strong").filter(has_text=kind)
                    ).to_be_visible()
                page.get_by_label(
                    "I reviewed the claim, reasoning, confidence and cited evidence.",
                    exact=True,
                ).check()
                page.get_by_role("button", name="Approve claim", exact=True).click()
                expect(page.get_by_role("alert")).to_contain_text(
                    "Explicitly acknowledge"
                )
                assert call(f"/claims/{claim_id}")["claim"]["status"] == "PROPOSED"
                page.get_by_label(
                    "I acknowledge the unresolved contradictions; approval does not resolve them.",
                    exact=True,
                ).check()
                page.get_by_role("button", name="Approve claim", exact=True).click()
                expect(
                    page.get_by_text("Claim action completed: approve.", exact=True)
                ).to_be_visible()
                page.get_by_label("Claim status filter", exact=True).select_option(
                    "APPROVED"
                )
                page.get_by_label("Claim confidence filter", exact=True).select_option(
                    "MODERATE"
                )
                page.get_by_label("Unresolved contradictions only", exact=True).check()
                page.get_by_role(
                    "button", name="Apply claim filters", exact=True
                ).click()
                expect(
                    page.get_by_role("button", name=statement, exact=True)
                ).to_be_visible()
                expect(
                    page.locator("figure.citation strong").filter(has_text="SUPPORTS")
                ).to_be_visible()
                page.screenshot(
                    path=str(local / "phase6-assessments.png"), full_page=True
                )
                page.goto(web_url + f"/investigations/{inv}/reports")
                expect(page.locator("[data-report-ready]")).to_have_attribute(
                    "data-report-ready", "true", timeout=20000
                )
                page.get_by_role(
                    "button", name="Validate report content", exact=True
                ).click()
                expect(
                    page.get_by_role("heading", name="Validation passed", exact=True)
                ).to_be_visible()
                page.get_by_label(
                    "I reviewed the approved content, confidence, contradictions and limitations for this report snapshot.",
                    exact=True,
                ).check()
                page.get_by_role(
                    "button", name="Generate finalized Markdown", exact=True
                ).click()
                expect(
                    page.get_by_text("Finalized Markdown snapshot saved.", exact=True)
                ).to_be_visible()
                preview = page.get_by_label("Markdown report preview", exact=True)
                expect(preview).to_contain_text("UNRESOLVED CONTRADICTION")
                expect(preview).to_contain_text("No approved events")
                expect(preview).to_contain_text("manual reliability A")
                report_id = call(f"/investigations/{inv}/reports")["items"][0]["id"]
                artifact = call(f"/reports/{report_id}")
                with page.expect_download() as download_info:
                    page.get_by_role(
                        "link", name="Download selected Markdown", exact=True
                    ).click()
                download = download_info.value
                content = download.path().read_bytes()
                assert content.decode() == artifact["markdown"]
                assert (
                    hashlib.sha256(content).hexdigest()
                    == artifact["report"]["contentHash"]
                )
                assert download.suggested_filename.endswith(".md")
                page.screenshot(path=str(local / "phase6-reports.png"), full_page=True)
                rendered = MarkdownIt("commonmark", {"html": True}).render(
                    artifact["markdown"]
                )
                rendered_page = browser.new_page()
                rendered_page.route("**/*", lambda intercepted: intercepted.abort())
                rendered_page.set_content(rendered)
                for heading in [
                    "Executive Summary",
                    "Scope and Research Questions",
                    "Key Findings",
                    "Relevant Entities",
                    "Chronological Events",
                    "Geographic Context",
                    "Source Evaluation",
                    "Conflicting Information",
                    "Analytical Limitations",
                    "References",
                ]:
                    expect(
                        rendered_page.get_by_role("heading", name=heading, exact=True)
                    ).to_be_visible()
                assert (
                    rendered_page.locator("script, img, a[href^='javascript:']").count()
                    == 0
                )
                targets = rendered_page.locator('a[href^="#evidence-"]').evaluate_all(
                    "nodes => nodes.map(node => node.getAttribute('href').substring(1))"
                )
                assert targets and all(
                    rendered_page.locator(f'[id="{target}"]').count() == 1
                    for target in targets
                )
                assert (
                    "[bad](javascript:alert(1))"
                    in rendered_page.locator("body").inner_text()
                )
                rendered_page.close()
                # Corrections withdraw approval; existing exported snapshots remain byte-identical.
                page.goto(web_url + f"/investigations/{inv}/assessments")
                expect(page.locator("[data-assessment-ready]")).to_have_attribute(
                    "data-assessment-ready", "true", timeout=20000
                )
                page.get_by_role("button", name=statement, exact=True).click()
                page.get_by_label("Analyst confidence", exact=True).select_option("LOW")
                page.get_by_role(
                    "button", name="Save claim proposal", exact=True
                ).click()
                expect(
                    page.get_by_text("Claim saved as proposed.", exact=True)
                ).to_be_visible()
                assert call(f"/claims/{claim_id}")["claim"]["status"] == "PROPOSED"
                contradictory = page.locator("figure.citation").filter(
                    has=page.locator("strong", has_text="CONTRADICTS")
                )
                contradictory.get_by_role(
                    "button", name="Edit evidence label / note", exact=True
                ).click()
                page.get_by_label("Evidence relationship", exact=True).select_option(
                    "CONTEXT"
                )
                page.get_by_label("Analyst evidence note", exact=True).fill(
                    "Reclassified after comparing local-date context."
                )
                page.get_by_role(
                    "button", name="Save evidence label", exact=True
                ).click()
                expect(
                    page.get_by_text(
                        "Evidence saved. Review the claim again before approval.",
                        exact=True,
                    )
                ).to_be_visible()
                assert not call(f"/claims/{claim_id}")["claim"][
                    "unresolvedContradictions"
                ]
                context = page.locator("figure.citation").filter(
                    has=page.get_by_role(
                        "link", name="Fictional context note", exact=True
                    )
                )
                context.get_by_role(
                    "button", name="Remove evidence", exact=True
                ).click()
                expect(page.locator("figure.citation")).to_have_count(2)
                assert len(call(f"/claims/{claim_id}")["evidence"]) == 2
                page.goto(web_url + f"/investigations/{inv}/reports")
                expect(page.locator("[data-report-ready]")).to_have_attribute(
                    "data-report-ready", "true", timeout=20000
                )
                page.get_by_role(
                    "button", name="Validate report content", exact=True
                ).click()
                expect(
                    page.get_by_role(
                        "heading", name="Validation blocked export", exact=True
                    )
                ).to_be_visible()
                assert call(f"/reports/{report_id}")["markdown"] == artifact["markdown"]
                assert not external and not errors, (external, errors)
                browser.close()
            stop(api)
            api = start("Argus.Api", api_port, env, log)
            wait_for(api_url + "/health", api)
            assert call(f"/reports/{report_id}")["markdown"] == artifact["markdown"]
            assert call(f"/claims/{claim_id}")["claim"]["confidenceLevel"] == "LOW"
            expect_error(
                lambda: request(route + f"/investigations/{inv}/collaboration"), 404
            )
            print(
                "PASS: real claim editor, supporting/contradictory/context passages, source reliability, explicit confidence/review, unresolved acknowledgment, filters, report validation, immutable Markdown download/hash, CommonMark rendering/anchors, escaped malicious content, correction withdrawal and API restart persistence; no multi-user endpoints."
            )
        finally:
            stop(web)
            stop(api)


if __name__ == "__main__":
    main()
