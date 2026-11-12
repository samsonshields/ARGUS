import hashlib
from uuid import uuid4

import pytest
from fastapi.testclient import TestClient

from app.main import create_app
from app.pipelines.events import interpret


def payload(text):
    return {
        "contractVersion": "1",
        "sourceId": str(uuid4()),
        "textHash": hashlib.sha256(text.encode()).hexdigest(),
        "text": text,
    }


@pytest.fixture
def client():
    with TestClient(create_app("argus_missing_model_test")) as instance:
        yield instance


@pytest.mark.parametrize(
    "text,precision,value",
    [
        ("2024-02-29", "EXACT", "2024-02-29"),
        ("March 12, 2026", "EXACT", "2026-03-12"),
        ("12 March 2026", "EXACT", "2026-03-12"),
        ("March 2026", "MONTH", "2026-03"),
        ("2026-02", "MONTH", "2026-02"),
        ("2026", "YEAR", "2026"),
        ("last Tuesday", "UNKNOWN", None),
        ("yesterday", "UNKNOWN", None),
        ("03/04/2026", "UNKNOWN", None),
        ("March 12", "UNKNOWN", None),
        ("2026-02-30", "UNKNOWN", None),
    ],
)
def test_explicit_precision_and_no_invented_reference_or_locale(client, text, precision, value):
    response = client.post("/process/events", json=payload(f"A meeting occurred on {text}."))
    assert response.status_code == 200
    (candidate,) = response.json()["events"]
    assert candidate["dateText"] == text
    assert candidate["precision"] == precision and candidate["dateValue"] == value
    assert candidate["confidence"] is None and candidate["reason"]


def test_exact_utf16_quotes_and_multiple_dates(client):
    text = "\U0001f600 Aster met on 2026-01-10. Later, in March 2026, another meeting occurred."
    body = client.post("/process/events", json=payload(text)).json()
    assert body["offsetConvention"] == "UTF16_CODE_UNITS" and body["method"] == "date_mentions_v1"
    assert len(body["events"]) == 2
    encoded = text.encode("utf-16-le")
    for item in body["events"]:
        assert encoded[item["start"] * 2 : item["end"] * 2].decode("utf-16-le") == item["dateText"]
        assert (
            encoded[item["sentenceStart"] * 2 : item["sentenceEnd"] * 2].decode("utf-16-le")
            == item["quote"]
        )
    assert body["events"][0]["quote"].startswith("\U0001f600")


def test_invalid_hash_empty_results_and_missing_ner_model(client):
    request = payload("No date asserted.")
    assert client.post("/process/events", json=request).json()["events"] == []
    request["textHash"] = "0" * 64
    response = client.post("/process/events", json=request)
    assert response.status_code == 422 and "No date asserted" not in response.text


def test_limits_and_sanitized_failure(client, monkeypatch):
    assert client.post("/process/events", json=payload("2026. " * 2001)).status_code == 422
    assert client.post("/process/events", json=payload("x " * 125001)).status_code == 422

    def fail(*args):
        raise RuntimeError("private parser diagnostics")

    monkeypatch.setattr("app.main.extract_events", fail)
    response = client.post("/process/events", json=payload("2026-01-01"))
    assert response.status_code == 500 and "private parser" not in response.text


def test_invalid_calendar_preserves_original_expression():
    assert interpret("February 30, 2026")[:2] == ("UNKNOWN", None)
