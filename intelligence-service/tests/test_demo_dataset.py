import hashlib
import json
from pathlib import Path
from uuid import NAMESPACE_URL, uuid4, uuid5

import pytest
import spacy

from app.models.relationships import RelationshipRequest
from app.pipelines.entities import extract_entities, load_pipeline
from app.pipelines.events import extract_events
from app.pipelines.relationships import extract_relationships

ROOT = Path(__file__).resolve().parents[2] / "sample-data/operation-meridian"
FIXTURE = json.loads((ROOT / "manifest.json").read_text(encoding="utf-8"))


@pytest.fixture(scope="module")
def extracted():
    model = load_pipeline("en_core_web_sm")
    return {
        document["key"]: (text, extract_entities(model, text))
        for document in FIXTURE["documents"]
        for text in [(ROOT / document["file"]).read_text(encoding="utf-8")]
    }


def test_real_model_proposes_every_training_identity_with_exact_unicode_spans(extracted):
    recognized = set()
    for text, candidates in extracted.values():
        assert text.startswith("FICTIONAL TRAINING DOCUMENT")
        encoded = text.encode("utf-16-le")
        for candidate in candidates:
            recognized.add(candidate.text)
            assert (
                encoded[candidate.start * 2 : candidate.end * 2].decode("utf-16-le")
                == candidate.text
            )
            assert candidate.confidence is None
    for entity in FIXTURE["entities"]:
        assert entity["name"] in recognized
        assert all(alias in recognized for alias in entity["aliases"])


def test_real_sentence_pipeline_reconstructs_fixture_relationships_after_review_and_merge(
    extracted,
):
    pipeline = spacy.blank("en")
    pipeline.add_pipe("sentencizer")
    canonical = {
        name: str(uuid5(NAMESPACE_URL, "argus-training/" + entity["key"]))
        for entity in FIXTURE["entities"]
        for name in [entity["name"], *entity["aliases"]]
    }
    pairs = set()
    for text, candidates in extracted.values():
        payload = RelationshipRequest.model_validate(
            {
                "contractVersion": "1",
                "sourceId": str(uuid4()),
                "textHash": hashlib.sha256(text.encode()).hexdigest(),
                "text": text,
                "mentions": [
                    {
                        "id": str(uuid4()),
                        "entityId": canonical[candidate.text],
                        "text": candidate.text,
                        "start": candidate.start,
                        "end": candidate.end,
                    }
                    for candidate in candidates
                    if candidate.text in canonical
                ],
            }
        )
        for candidate in extract_relationships(pipeline, payload):
            assert candidate.type == "MENTIONED_WITH" and candidate.confidence is None
            pairs.add(frozenset((str(candidate.sourceEntityId), str(candidate.targetEntityId))))
    by_key = {entity["key"]: canonical[entity["name"]] for entity in FIXTURE["entities"]}
    expected = {
        frozenset((by_key[pair["source"]], by_key[pair["target"]]))
        for pair in FIXTURE["relationships"]
    }
    assert pairs == expected


def test_training_dates_and_evidence_references_retain_uncertainty(extracted):
    pipeline = spacy.blank("en")
    pipeline.add_pipe("sentencizer")
    dates = {key: extract_events(pipeline, text) for key, (text, _) in extracted.items()}
    assert sum(len(items) for items in dates.values()) == 12
    for event in FIXTURE["events"]:
        candidates = [
            item for item in dates[event["document"]] if item.dateText == event["dateText"]
        ]
        assert len(candidates) == 1 and candidates[0].precision == event["precision"]
    contradictory_documents = set()
    for claim in FIXTURE["claims"]:
        assert any(evidence["relationship"] == "SUPPORTS" for evidence in claim["evidence"])
        for evidence in claim["evidence"]:
            assert evidence["document"] in extracted
            if evidence["relationship"] == "CONTRADICTS":
                contradictory_documents.add(evidence["document"])
    assert len(contradictory_documents) == 2
