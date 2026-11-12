import asyncio
import os
from contextlib import asynccontextmanager
from threading import Lock
from typing import AsyncIterator

import spacy
from fastapi import FastAPI, Request
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse
from spacy.language import Language
from starlette.types import ASGIApp, Message, Receive, Scope, Send

from app.logging_utils import application_logger
from app.models.entities import EntityRequest, EntityResponse, ModelInfo
from app.models.events import EventResponse
from app.models.relationships import RelationshipRequest, RelationshipResponse
from app.pipelines.entities import ExtractionLimitError, extract_entities, load_pipeline
from app.pipelines.events import extract_events
from app.pipelines.relationships import extract_relationships

logger = application_logger()
MAX_BODY = 12_100_000


def problem(status: int, code: str, title: str, **extra: object) -> JSONResponse:
    return JSONResponse(
        status_code=status,
        media_type="application/problem+json",
        content={
            "status": status,
            "code": code,
            "title": title,
            **extra,
        },
    )


class BodyLimitMiddleware:
    """Bound streamed bodies before JSON parsing; never log submitted text."""

    def __init__(self, app: ASGIApp):
        self.app = app

    async def __call__(self, scope: Scope, receive: Receive, send: Send) -> None:
        if scope["type"] != "http":
            await self.app(scope, receive, send)
            return
        messages: list[Message] = []
        size = 0
        try:
            while True:
                message = await asyncio.wait_for(receive(), timeout=15)
                if message["type"] == "http.disconnect":
                    return
                size += len(message.get("body", b""))
                if size > MAX_BODY:
                    await problem(413, "body_too_large", "Request body is too large.")(
                        scope, receive, send
                    )
                    return
                messages.append(message)
                if not message.get("more_body", False):
                    break
        except TimeoutError:
            await problem(408, "body_timeout", "Request body timed out.")(scope, receive, send)
            return

        index = 0

        async def replay() -> Message:
            nonlocal index
            if index < len(messages):
                message = messages[index]
                index += 1
                return message
            return await receive()

        await self.app(scope, replay, send)


def create_app(model_name: str | None = None) -> FastAPI:
    name = model_name or os.getenv("ARGUS_SPACY_MODEL", "en_core_web_sm")
    max_characters = int(os.getenv("ARGUS_NLP_MAX_UTF16", "250000"))
    if not 1 <= max_characters <= 2_000_000:
        raise ValueError("ARGUS_NLP_MAX_UTF16 must be between 1 and 2000000.")
    gate = Lock()
    sentence_pipeline = spacy.blank("en")
    sentence_pipeline.add_pipe("sentencizer")
    sentence_pipeline.max_length = 2_000_000

    @asynccontextmanager
    async def lifespan(application: FastAPI) -> AsyncIterator[None]:
        application.state.pipeline = None
        try:
            application.state.pipeline = load_pipeline(name)
        except Exception:
            logger.error("NLP model unavailable; install a compatible trained English spaCy model.")
        yield

    application = FastAPI(title="ARGUS local intelligence service", version="1", lifespan=lifespan)
    application.add_middleware(BodyLimitMiddleware)

    @application.exception_handler(RequestValidationError)
    async def invalid_request(_: Request, error: RequestValidationError) -> JSONResponse:
        # Pydantic input/errors can include the full document; omit those values.
        errors = [
            {"field": ".".join(map(str, item["loc"])), "code": item["type"]}
            for item in error.errors()
        ]
        return problem(422, "invalid_request", "Invalid entity-processing request.", errors=errors)

    @application.get("/health", response_model=None)
    def health() -> dict[str, object] | JSONResponse:
        pipeline = application.state.pipeline
        if pipeline is None:
            return problem(
                503,
                "model_unavailable",
                "Install the configured English spaCy model and restart the service.",
            )
        return {"status": "ready", "contractVersion": "1", "model": model_info(pipeline)}

    def model_info(pipeline: Language) -> ModelInfo:
        return ModelInfo(
            name=name,
            version=pipeline.meta.get("version", "unknown"),
            spacyVersion=spacy.__version__,
        )

    @application.post("/process/entities", response_model=EntityResponse)
    def entities(payload: EntityRequest) -> EntityResponse | JSONResponse:
        pipeline = application.state.pipeline
        if pipeline is None:
            return problem(
                503, "model_unavailable", "The configured English spaCy model is unavailable."
            )
        if len(payload.text.encode("utf-16-le")) // 2 > max_characters:
            return problem(
                422,
                "text_limit",
                "Text exceeds the configured NLP character limit; it was not truncated.",
            )
        if not gate.acquire(blocking=False):
            return problem(
                503, "service_busy", "The local NLP service is processing another document."
            )
        try:
            candidates = extract_entities(pipeline, payload.text)
            return EntityResponse(
                sourceId=payload.sourceId,
                textHash=payload.textHash,
                model=model_info(pipeline),
                entities=candidates,
            )
        except ExtractionLimitError:
            return problem(422, "candidate_limit", "Entity results exceeded the processing limits.")
        except Exception:
            logger.error(
                "Entity pipeline failed; submitted text and parser diagnostics omitted.",
                extra={"SourceId": str(payload.sourceId), "Pipeline": "entities"},
            )
            return problem(500, "pipeline_failed", "The local entity pipeline failed.")
        finally:
            gate.release()

    @application.post("/process/relationships", response_model=RelationshipResponse)
    def relationships(payload: RelationshipRequest) -> RelationshipResponse | JSONResponse:
        if len(payload.text.encode("utf-16-le")) // 2 > max_characters:
            return problem(422, "text_limit", "Text exceeds the configured relationship limit.")
        if not gate.acquire(blocking=False):
            return problem(
                503, "service_busy", "The local pipeline is processing another document."
            )
        try:
            return RelationshipResponse(
                sourceId=payload.sourceId,
                textHash=payload.textHash,
                relationships=extract_relationships(sentence_pipeline, payload),
            )
        except ExtractionLimitError:
            return problem(
                422, "candidate_limit", "Co-mention candidate or passage limit exceeded."
            )
        except Exception:
            logger.error(
                "Co-mention generation failed; document and parser diagnostics omitted.",
                extra={"SourceId": str(payload.sourceId), "Pipeline": "relationships"},
            )
            return problem(500, "pipeline_failed", "Local co-mention generation failed.")
        finally:
            gate.release()

    @application.post("/process/events", response_model=EventResponse)
    def events(payload: EntityRequest) -> EventResponse | JSONResponse:
        if len(payload.text.encode("utf-16-le")) // 2 > max_characters:
            return problem(422, "text_limit", "Text exceeds the configured event extraction limit.")
        if not gate.acquire(blocking=False):
            return problem(
                503, "service_busy", "The local pipeline is processing another document."
            )
        try:
            return EventResponse(
                sourceId=str(payload.sourceId),
                textHash=payload.textHash,
                events=extract_events(sentence_pipeline, payload.text),
            )
        except ExtractionLimitError:
            return problem(
                422, "candidate_limit", "Date candidate or source passage limit exceeded."
            )
        except Exception:
            logger.error(
                "Date candidate extraction failed; document diagnostics omitted.",
                extra={"SourceId": str(payload.sourceId), "Pipeline": "events"},
            )
            return problem(500, "pipeline_failed", "Local date candidate extraction failed.")
        finally:
            gate.release()

    return application


app = create_app()
