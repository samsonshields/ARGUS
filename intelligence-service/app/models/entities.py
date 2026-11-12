import hashlib
from typing import Literal
from uuid import UUID

from pydantic import BaseModel, ConfigDict, Field, model_validator

EntityType = Literal["PERSON", "ORGANIZATION", "LOCATION", "DATE", "EVENT", "OTHER"]


class EntityRequest(BaseModel):
    model_config = ConfigDict(extra="forbid", strict=True)
    contractVersion: Literal["1"]
    sourceId: UUID = Field(strict=False)
    text: str = Field(max_length=2_000_000)
    textHash: str = Field(pattern=r"^[0-9a-f]{64}$")

    @model_validator(mode="after")
    def validate_text(self) -> "EntityRequest":
        try:
            digest = hashlib.sha256(self.text.encode("utf-8", errors="strict")).hexdigest()
        except UnicodeError as error:
            raise ValueError("Text must contain valid Unicode.") from error
        if digest != self.textHash:
            raise ValueError("textHash must match the exact UTF-8 text.")
        return self


class EntityCandidate(BaseModel):
    model_config = ConfigDict(extra="forbid")
    text: str = Field(min_length=1, max_length=2000)
    type: EntityType
    start: int = Field(ge=0)
    end: int = Field(gt=0)
    confidence: None = None


class ModelInfo(BaseModel):
    name: str
    version: str
    spacyVersion: str


class EntityResponse(BaseModel):
    contractVersion: Literal["1"] = "1"
    sourceId: UUID
    textHash: str
    offsetConvention: Literal["UTF16_CODE_UNITS"] = "UTF16_CODE_UNITS"
    model: ModelInfo
    entities: list[EntityCandidate]
