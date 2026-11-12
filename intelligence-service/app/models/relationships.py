from typing import Literal
from uuid import UUID

from pydantic import BaseModel, ConfigDict, Field, model_validator

from app.models.entities import EntityRequest


class ApprovedMention(BaseModel):
    model_config = ConfigDict(extra="forbid", strict=True)
    id: UUID = Field(strict=False)
    entityId: UUID = Field(strict=False)
    text: str = Field(min_length=1, max_length=2000)
    start: int = Field(ge=0)
    end: int = Field(gt=0)


class RelationshipRequest(EntityRequest):
    mentions: list[ApprovedMention] = Field(max_length=5000)

    @model_validator(mode="after")
    def validate_mentions(self) -> "RelationshipRequest":
        encoded = self.text.encode("utf-16-le")
        seen: set[UUID] = set()
        for mention in self.mentions:
            if mention.id in seen or mention.end <= mention.start or mention.end * 2 > len(encoded):
                raise ValueError("Invalid or duplicate mention span.")
            seen.add(mention.id)
            try:
                passage = encoded[mention.start * 2 : mention.end * 2].decode("utf-16-le")
            except UnicodeError as error:
                raise ValueError("Mention splits a Unicode character.") from error
            if passage != mention.text:
                raise ValueError("Mention does not match the exact snapshot text.")
        return self


class RelationshipCandidate(BaseModel):
    sourceMentionId: UUID
    targetMentionId: UUID
    sourceEntityId: UUID
    targetEntityId: UUID
    type: Literal["MENTIONED_WITH"] = "MENTIONED_WITH"
    start: int
    end: int
    quote: str
    confidence: None = None


class RelationshipResponse(BaseModel):
    contractVersion: Literal["1"] = "1"
    sourceId: UUID
    textHash: str
    offsetConvention: Literal["UTF16_CODE_UNITS"] = "UTF16_CODE_UNITS"
    method: Literal["sentence_cooccurrence_v1"] = "sentence_cooccurrence_v1"
    relationships: list[RelationshipCandidate]
