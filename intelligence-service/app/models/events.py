from typing import Literal

from pydantic import BaseModel


class EventCandidate(BaseModel):
    dateText: str
    start: int
    end: int
    sentenceStart: int
    sentenceEnd: int
    quote: str
    precision: Literal["EXACT", "MONTH", "YEAR", "UNKNOWN"]
    dateValue: str | None
    reason: str
    confidence: None = None


class EventResponse(BaseModel):
    contractVersion: Literal["1"] = "1"
    sourceId: str
    textHash: str
    offsetConvention: Literal["UTF16_CODE_UNITS"] = "UTF16_CODE_UNITS"
    method: Literal["date_mentions_v1"] = "date_mentions_v1"
    events: list[EventCandidate]
