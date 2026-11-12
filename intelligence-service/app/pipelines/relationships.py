from itertools import combinations

from spacy.language import Language

from app.models.relationships import RelationshipCandidate, RelationshipRequest
from app.pipelines.entities import ExtractionLimitError


def extract_relationships(
    nlp: Language, payload: RelationshipRequest, limit: int = 5000
) -> list[RelationshipCandidate]:
    """Same-sentence occurrence only; no semantic assertion, score or automatic approval."""
    utf16 = [0]
    for char in payload.text:
        utf16.append(utf16[-1] + (2 if ord(char) > 0xFFFF else 1))
    result: list[RelationshipCandidate] = []
    mentions = sorted(payload.mentions, key=lambda item: (item.start, item.end, str(item.id)))
    for sentence in nlp(payload.text).sents:
        start, end = utf16[sentence.start_char], utf16[sentence.end_char]
        distinct = {}
        for mention in mentions:
            if start <= mention.start and mention.end <= end:
                distinct.setdefault(mention.entityId, mention)
        if len(distinct) < 2:
            continue
        if end - start > 8000:
            raise ExtractionLimitError()
        ordered = sorted(distinct.values(), key=lambda item: str(item.entityId))
        for first, second in combinations(ordered, 2):
            if len(result) >= limit:
                raise ExtractionLimitError()
            result.append(
                RelationshipCandidate(
                    sourceMentionId=first.id,
                    targetMentionId=second.id,
                    sourceEntityId=first.entityId,
                    targetEntityId=second.entityId,
                    start=start,
                    end=end,
                    quote=sentence.text,
                )
            )
    return result
