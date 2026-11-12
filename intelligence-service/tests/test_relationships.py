import hashlib
from uuid import uuid4

import pytest
from fastapi.testclient import TestClient

from app.main import create_app


def request(text: str, names: list[tuple[str, str]]) -> dict:
    mentions = []
    for name, entity in names:
        start = text.index(name)
        mentions.append(
            {
                "id": str(uuid4()),
                "entityId": entity,
                "text": name,
                "start": len(text[:start].encode("utf-16-le")) // 2,
                "end": len(text[: start + len(name)].encode("utf-16-le")) // 2,
            }
        )
    return {
        "contractVersion": "1",
        "sourceId": str(uuid4()),
        "textHash": hashlib.sha256(text.encode()).hexdigest(),
        "text": text,
        "mentions": mentions,
    }


@pytest.fixture
def client():
    # Co-mention generation does not fabricate NER or depend on trained NER weights.
    with TestClient(create_app("argus_missing_model_test")) as test_client:
        yield test_client


def test_same_sentence_utf16_provenance_and_low_assertion(client):
    text = "\U0001f600 Alice met Aster. Bob departed."
    payload = request(
        text, [("Alice", str(uuid4())), ("Aster", str(uuid4())), ("Bob", str(uuid4()))]
    )
    response = client.post("/process/relationships", json=payload)
    assert response.status_code == 200
    body = response.json()
    assert body["method"] == "sentence_cooccurrence_v1"
    assert len(body["relationships"]) == 1
    candidate = body["relationships"][0]
    assert candidate["type"] == "MENTIONED_WITH" and candidate["confidence"] is None
    encoded = text.encode("utf-16-le")
    assert (
        encoded[candidate["start"] * 2 : candidate["end"] * 2].decode("utf-16-le")
        == candidate["quote"]
    )
    assert "Bob" not in candidate["quote"]


def test_duplicate_mentions_of_one_entity_do_not_create_self_edges(client):
    entity = str(uuid4())
    result = client.post(
        "/process/relationships",
        json=request("Alice met Aster.", [("Alice", entity), ("Aster", entity)]),
    )
    assert result.status_code == 200 and result.json()["relationships"] == []


@pytest.mark.parametrize(
    "text,names",
    [("", []), ("Alice left. Bob arrived.", ["Alice", "Bob"]), ("Alice left.", ["Alice"])],
)
def test_empty_separate_and_single_entities_have_no_candidates(client, text, names):
    payload = request(text, [(name, str(uuid4())) for name in names])
    assert client.post("/process/relationships", json=payload).json()["relationships"] == []


@pytest.mark.parametrize("change", ["text", "offset", "duplicate", "unicode"])
def test_untrusted_mention_spans_fail_without_echoing_input(client, change):
    payload = request(
        "\U0001f600 PRIVATE_INPUT met Aster.",
        [("PRIVATE_INPUT", str(uuid4())), ("Aster", str(uuid4()))],
    )
    if change == "text":
        payload["mentions"][0]["text"] = "Wrong"
    elif change == "offset":
        payload["mentions"][0]["end"] = 99999
    elif change == "duplicate":
        payload["mentions"].append(payload["mentions"][0])
    else:
        payload["mentions"][0].update(start=1, end=2, text="x")
    response = client.post("/process/relationships", json=payload)
    assert response.status_code == 422
    assert "PRIVATE_INPUT" not in response.text


def test_pairs_are_deterministic_and_order_independent(client):
    payload = request(
        "Alice met Aster and Bob.", [(name, str(uuid4())) for name in ["Alice", "Aster", "Bob"]]
    )
    first = client.post("/process/relationships", json=payload).json()
    payload["mentions"].reverse()
    assert client.post("/process/relationships", json=payload).json() == first
    assert len(first["relationships"]) == 3
