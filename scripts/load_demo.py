"""Load the fictional Operation Meridian exercise through the real public ARGUS APIs."""

from __future__ import annotations

import argparse
import json
import time
from pathlib import Path
from urllib.error import HTTPError
from urllib.parse import urlparse

from smoke import ROOT, request, upload


def load_demo(api_url: str, output: Path | None = None) -> dict:
    if urlparse(api_url).hostname not in {"localhost", "127.0.0.1", "::1"}:
        raise ValueError("The training loader accepts loopback API addresses only.")
    route = api_url.rstrip("/") + "/api/v1"
    root = ROOT / "sample-data/operation-meridian"
    fixture = json.loads((root / "manifest.json").read_text(encoding="utf-8"))

    def call(path: str, method: str = "GET", body: dict | None = None):
        result = request(route + path, method, body)
        return json.loads(result) if result else None

    def pages(path: str) -> list:
        result = []
        page = 1
        while True:
            data = call(
                path + ("&" if "?" in path else "?") + f"page={page}&pageSize=100"
            )
            result.extend(data["items"])
            if len(result) >= data["total"]:
                return result
            page += 1

    def finished(job: dict) -> None:
        deadline = time.monotonic() + 120
        while time.monotonic() < deadline:
            current = call(f"/processing-jobs/{job['id']}")
            if current["state"] == "COMPLETED":
                return
            if current["state"] in {"FAILED", "CANCELLED"}:
                raise RuntimeError(
                    f"Training job failed: {current['errorCode']}. Check worker/model settings."
                )
            time.sleep(0.2)
        raise TimeoutError(
            "Training job timed out. Ensure Intelligence__WorkerEnabled=true."
        )

    call("/intelligence/health")
    investigation = call(
        "/investigations",
        "POST",
        {
            "title": fixture["title"],
            "description": fixture["description"],
            "region": fixture["region"],
            "startDate": "2026-01-01",
            "endDate": "2026-12-31",
        },
    )
    inv = investigation["id"]
    result = {
        "investigationId": inv,
        "sources": {},
        "entities": {},
        "locations": {},
        "events": [],
        "claims": [],
        "reportId": None,
    }

    def checkpoint() -> None:
        if output is not None:
            output.parent.mkdir(parents=True, exist_ok=True)
            output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")

    checkpoint()
    for document in fixture["documents"]:
        path = (root / document["file"]).resolve()
        if not path.is_relative_to(root.resolve()):
            raise ValueError("Training source escaped the fixture directory.")
        source = upload(
            route + f"/investigations/{inv}/sources",
            path.read_bytes(),
            path.name,
            document["title"],
        )["source"]["id"]
        result["sources"][document["key"]] = source
        call(
            f"/sources/{source}",
            "PUT",
            {
                "title": document["title"],
                "publisher": document["publisher"],
                "publishedAt": document["publishedAt"],
                "reliability": document["reliability"],
            },
        )
        jobs = call(f"/processing-jobs?sourceId={source}")["items"]
        job = next((job for job in jobs if job["kind"] == "ENTITIES"), None)
        if job is None:
            job = call(f"/sources/{source}/process", "POST")
        finished(job)
        checkpoint()
    identities = {entity["name"]: entity for entity in fixture["entities"]}
    aliases = {
        alias: entity for entity in fixture["entities"] for alias in entity["aliases"]
    }
    alias_entities = {}
    for mention in pages(f"/investigations/{inv}/mentions?status=PROPOSED"):
        name = mention["text"]
        definition = identities.get(name) or aliases.get(name)
        if definition is None:
            continue
        is_alias = name in aliases
        chosen = (
            alias_entities.get(name)
            if is_alias
            else result["entities"].get(definition["key"])
        )
        entity = call(
            f"/mentions/{mention['id']}/approve",
            "POST",
            {"name": name, "type": definition["type"], "entityId": chosen},
        )
        if is_alias:
            alias_entities[name] = entity["id"]
        else:
            result["entities"][definition["key"]] = entity["id"]
    missing = [
        entity["name"]
        for entity in fixture["entities"]
        if entity["key"] not in result["entities"]
    ]
    if missing:
        raise RuntimeError(
            "Installed NER model did not propose these training names: "
            + ", ".join(missing)
            + ". Use the documented model version; no entities were fabricated."
        )
    if not alias_entities:
        raise RuntimeError(
            "The installed model did not propose the alias needed for the merge exercise."
        )
    result["merges"] = []
    for alias, alias_id in alias_entities.items():
        target = result["entities"][aliases[alias]["key"]]
        call(
            "/entities/merge",
            "POST",
            {"sourceId": alias_id, "targetId": target, "confirm": True},
        )
        result["merges"].append({"sourceId": alias_id, "targetId": target})
    checkpoint()
    generation = call(f"/investigations/{inv}/relationships/generate", "POST")
    for job in generation["jobs"]:
        finished(job)
    relationships = pages(f"/investigations/{inv}/relationships")
    expected_pairs = {
        frozenset(
            (result["entities"][pair["source"]], result["entities"][pair["target"]])
        )
        for pair in fixture["relationships"]
    }
    actual_pairs = {
        frozenset((item["sourceEntityId"], item["targetEntityId"]))
        for item in relationships
    }
    if actual_pairs != expected_pairs:
        raise RuntimeError(
            f"Training co-mention pairs differ: expected {len(expected_pairs)}, received {len(actual_pairs)}."
        )
    for review in fixture["reviewedRelationships"]:
        source, target = (
            result["entities"][review["source"]],
            result["entities"][review["target"]],
        )
        edge = next(
            item
            for item in relationships
            if {item["sourceEntityId"], item["targetEntityId"]} == {source, target}
        )
        detail = call(f"/relationships/{edge['id']}")
        call(
            f"/relationships/{edge['id']}/approve",
            "POST",
            {
                "type": review["type"],
                "notes": review["notes"],
                "reverse": review["type"] == "MEMBER_OF"
                and edge["sourceEntityId"] != source,
                "confirmEvidenceReviewed": True,
                "evidenceIds": [proof["id"] for proof in detail["evidence"]],
            },
        )
    passages = {
        key: call(f"/sources/{source}/passages")["items"][0]
        for key, source in result["sources"].items()
    }
    for location in fixture["locations"]:
        item = call(
            f"/investigations/{inv}/locations",
            "POST",
            {
                "entityId": result["entities"][location["entity"]],
                "precision": location["precision"],
                "latitude": location["latitude"],
                "longitude": location["longitude"],
                "uncertaintyMeters": location["uncertaintyMeters"],
                "basis": "EXPLICIT_SELECTION",
                "confirmCoordinates": True,
                "notes": "Explicit hypothetical open-water training coordinate for a fictional location. It does not identify a real facility. The radius represents selected exercise uncertainty.",
            },
        )
        passage = passages[location["document"]]
        call(
            f"/locations/{item['id']}/evidence",
            "POST",
            {
                "snapshotId": passage["snapshotId"],
                "start": passage["start"],
                "end": passage["end"],
                "revision": item["revision"],
            },
        )
        item = call(f"/locations/{item['id']}")
        call(
            f"/locations/{item['id']}/approve",
            "POST",
            {"confirm": True, "revision": item["revision"]},
        )
        result["locations"][location["entity"]] = item["id"]
    for source in result["sources"].values():
        finished(call(f"/sources/{source}/process-events", "POST"))
    candidates = pages(f"/investigations/{inv}/events?status=PROPOSED")
    for definition in fixture["events"]:
        source = result["sources"][definition["document"]]
        item = next(
            candidate
            for candidate in candidates
            if candidate["originalDateText"] == definition["dateText"]
            and any(proof["sourceId"] == source for proof in candidate["evidence"])
        )
        value = item["startAt"][
            : {"EXACT": 10, "MONTH": 7, "YEAR": 4}[item["datePrecision"]]
        ]
        item = call(
            f"/events/{item['id']}",
            "PUT",
            {
                "title": definition["title"],
                "description": "Analyst-reviewed fictional training event. Exact, month-only and year-only precision are retained.",
                "type": definition["type"],
                "datePrecision": item["datePrecision"],
                "dateValue": value,
                "locationId": result["locations"].get(definition["location"]),
                "entityIds": [
                    result["entities"][key] for key in definition["entities"]
                ],
                "revision": item["revision"],
            },
        )
        call(
            f"/events/{item['id']}/approve",
            "POST",
            {"confirm": True, "revision": item["revision"]},
        )
        result["events"].append(item["id"])
    for definition in fixture["claims"]:
        item = call(
            f"/investigations/{inv}/claims",
            "POST",
            {
                "statement": definition["statement"],
                "reasoning": definition["reasoning"],
                "confidenceLevel": definition["confidence"],
            },
        )
        claim_id = item["claim"]["id"]
        for evidence in definition["evidence"]:
            passage = passages[evidence["document"]]
            call(
                f"/claims/{claim_id}/evidence",
                "POST",
                {
                    "snapshotId": passage["snapshotId"],
                    "start": passage["start"],
                    "end": passage["end"],
                    "relationship": evidence["relationship"],
                    "notes": "Fictional exercise evidence classification; source assertions are not independently verified.",
                    "revision": item["claim"]["revision"],
                },
            )
            item = call(f"/claims/{claim_id}")
        call(
            f"/claims/{claim_id}/approve",
            "POST",
            {
                "confirm": True,
                "acknowledgeUnresolvedContradictions": True,
                "revision": item["claim"]["revision"],
            },
        )
        result["claims"].append(claim_id)
    report = call(
        f"/investigations/{inv}/reports",
        "POST",
        {"confirmApprovedContentReviewed": True},
    )
    result["reportId"] = report["report"]["id"]
    result["expected"] = fixture["expected"]
    checkpoint()
    return result


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--api-url", default="http://localhost:5100")
    parser.add_argument(
        "--output", type=Path, default=ROOT / ".local/meridian-result.json"
    )
    args = parser.parse_args()
    try:
        result = load_demo(args.api_url, args.output)
        print(
            f"Loaded fictional investigation {result['investigationId']}; report {result['reportId']}. Saved record IDs to {args.output}."
        )
    except HTTPError as error:
        raise SystemExit(
            f"API returned HTTP {error.code}: {error.read().decode()}"
        ) from error


if __name__ == "__main__":
    main()
