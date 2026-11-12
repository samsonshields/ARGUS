import hashlib
from types import SimpleNamespace
from uuid import uuid4

import pytest
import spacy
from fastapi.testclient import TestClient
from spacy.tokens import Doc

import app.main as main
from app.pipelines.entities import ExtractionLimitError, extract_entities


def payload(text: str) -> dict:
    return {
        "contractVersion": "1",
        "sourceId": str(uuid4()),
        "text": text,
        "textHash": hashlib.sha256(text.encode()).hexdigest(),
    }


@pytest.fixture
def pipeline():
    nlp = spacy.blank("en")
    ruler = nlp.add_pipe("entity_ruler")
    ruler.add_patterns(
        [
            {"label": "PERSON", "pattern": "Alice Morgan"},
            {"label": "ORG", "pattern": "United Nations"},
        ]
    )
    nlp.meta["version"] = "test-1"
    return nlp


@pytest.fixture
def client(monkeypatch, pipeline):
    # Only tests substitute a deterministic pipeline. Production never falls back to rules.
    monkeypatch.setattr(main, "load_pipeline", lambda _: pipeline)
    with TestClient(main.create_app()) as test_client:
        yield test_client


@pytest.mark.parametrize(
    ("label", "expected"),
    [
        ("PERSON", "PERSON"),
        ("ORG", "ORGANIZATION"),
        ("GPE", "LOCATION"),
        ("LOC", "LOCATION"),
        ("FAC", "LOCATION"),
        ("DATE", "DATE"),
        ("TIME", "DATE"),
        ("EVENT", "EVENT"),
        ("NORP", "OTHER"),
        ("UNKNOWN", "OTHER"),
    ],
)
def test_label_mapping(label, expected):
    nlp = spacy.blank("en")
    doc = Doc(nlp.vocab, words=["Name"], ents=[f"B-{label}"])
    candidates = extract_entities(lambda _: doc, "Name")
    assert candidates[0].type == expected
    assert candidates[0].confidence is None


def test_exact_utf16_spans_and_repeated_mentions(client):
    text = "\U0001f600 Alice Morgan met United Nations. Alice Morgan spoke."
    request = payload(text)
    result = client.post("/process/entities", json=request)
    assert result.status_code == 200
    body = result.json()
    assert body["sourceId"] == request["sourceId"]
    assert body["textHash"] == request["textHash"]
    assert body["offsetConvention"] == "UTF16_CODE_UNITS"
    assert len(body["entities"]) == 3
    assert body["entities"][0]["start"] == 3
    for candidate in body["entities"]:
        encoded = text.encode("utf-16-le")
        assert (
            encoded[candidate["start"] * 2 : candidate["end"] * 2].decode("utf-16-le")
            == candidate["text"]
        )
        assert candidate["confidence"] is None


@pytest.mark.parametrize("text", ["", "  \n\t", "No recognized names here."])
def test_empty_or_no_entities_are_not_fabricated(client, text):
    result = client.post("/process/entities", json=payload(text))
    assert result.status_code == 200
    assert result.json()["entities"] == []


def test_duplicate_candidate_behavior_and_limits(pipeline):
    doc = pipeline("Alice Morgan")
    duplicate = SimpleNamespace(ents=[doc.ents[0], doc.ents[0]])
    assert len(extract_entities(lambda _: duplicate, "Alice Morgan")) == 1
    with pytest.raises(ExtractionLimitError):
        extract_entities(pipeline, "Alice Morgan", max_candidates=0)


@pytest.mark.parametrize(
    "change",
    [
        {"contractVersion": "2"},
        {"sourceId": "not-a-uuid"},
        {"textHash": "a" * 64},
        {"text": 123},
        {"unexpected": "PRIVATE_INPUT"},
        {"text": None},
    ],
)
def test_invalid_contracts_are_safe(client, change):
    request = payload("PRIVATE_INPUT")
    request.update(change)
    response = client.post("/process/entities", json=request)
    assert response.status_code == 422
    assert response.json()["code"] == "invalid_request"
    assert "PRIVATE_INPUT" not in response.text


def test_malformed_request_is_safe(client):
    response = client.post(
        "/process/entities",
        content='{"PRIVATE_INPUT":broken',
        headers={"content-type": "application/json"},
    )
    assert response.status_code == 422
    assert "PRIVATE_INPUT" not in response.text


def test_model_unavailable_is_readiness_error():
    with TestClient(main.create_app("argus_missing_model_for_test")) as client:
        assert client.get("/health").status_code == 503
        result = client.post("/process/entities", json=payload("Alice Morgan"))
        assert result.status_code == 503
        assert result.json()["code"] == "model_unavailable"
        assert "entities" not in result.json()


def test_health_and_openapi_are_available(client):
    assert client.get("/health").json()["status"] == "ready"
    paths = client.get("/openapi.json").json()["paths"]
    assert "/process/entities" in paths
    assert "/process/relationships" in paths
    assert "/process/events" not in paths


def test_limits_do_not_silently_truncate(monkeypatch, pipeline):
    monkeypatch.setenv("ARGUS_NLP_MAX_UTF16", "4")
    monkeypatch.setattr(main, "load_pipeline", lambda _: pipeline)
    with TestClient(main.create_app()) as client:
        assert (
            client.post("/process/entities", json=payload("\U0001f600" * 3)).json()["code"]
            == "text_limit"
        )


def test_body_limit_precedes_validation(monkeypatch, client):
    monkeypatch.setattr(main, "MAX_BODY", 100)
    assert client.post("/process/entities", content=b"x" * 101).status_code == 413


def test_pipeline_failure_does_not_echo_text(monkeypatch, client, caplog):
    def fail(*_):
        raise RuntimeError("PRIVATE_INPUT")

    monkeypatch.setattr(main, "extract_entities", fail)
    response = client.post("/process/entities", json=payload("PRIVATE_INPUT"))
    assert response.status_code == 500
    assert "PRIVATE_INPUT" not in response.text + caplog.text


def test_real_installed_english_model():
    with TestClient(main.create_app("en_core_web_sm")) as client:
        text = "Fictional training: Alice Morgan met a United Nations delegation in Geneva."
        result = client.post("/process/entities", json=payload(text))
        assert result.status_code == 200
        names = {candidate["text"] for candidate in result.json()["entities"]}
        assert {"Alice Morgan", "United Nations", "Geneva"}.issubset(names)
