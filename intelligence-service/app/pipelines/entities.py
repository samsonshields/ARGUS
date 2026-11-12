import spacy
from spacy.language import Language

from app.models.entities import EntityCandidate, EntityType

LABELS: dict[str, EntityType] = {
    "PERSON": "PERSON",
    "ORG": "ORGANIZATION",
    "GPE": "LOCATION",
    "LOC": "LOCATION",
    "FAC": "LOCATION",
    "DATE": "DATE",
    "TIME": "DATE",
    "EVENT": "EVENT",
}


class ExtractionLimitError(Exception):
    pass


def extract_entities(nlp: Language, text: str, max_candidates: int = 5000) -> list[EntityCandidate]:
    """Do not normalize text: returned spans index the exact input using UTF-16 units."""
    if not text.strip():
        return []
    utf16 = [0]
    for character in text:
        utf16.append(utf16[-1] + (2 if ord(character) > 0xFFFF else 1))
    result: list[EntityCandidate] = []
    seen: set[tuple[int, int, str]] = set()
    for entity in nlp(text).ents:
        entity_type = LABELS.get(entity.label_, "OTHER")
        key = (utf16[entity.start_char], utf16[entity.end_char], entity_type)
        if key in seen:
            continue
        seen.add(key)
        if len(result) >= max_candidates or len(entity.text) > 2000:
            raise ExtractionLimitError()
        result.append(EntityCandidate(text=entity.text, type=entity_type, start=key[0], end=key[1]))
    return sorted(result, key=lambda candidate: (candidate.start, candidate.end, candidate.type))


def load_pipeline(name: str) -> Language:
    pipeline = spacy.load(name, enable=["tok2vec", "ner"])
    if pipeline.lang != "en" or "ner" not in pipeline.pipe_names:
        raise ValueError("A trained named-entity recognizer is required.")
    pipeline.max_length = 2_000_000
    return pipeline
