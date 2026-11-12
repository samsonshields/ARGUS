import json
import logging
from datetime import datetime

from app.logging_utils import StructuredFormatter, application_logger


def test_structured_application_log_keeps_only_safe_diagnostic_fields():
    record = logging.LogRecord(
        "argus.intelligence", logging.ERROR, "unused", 1, "Pipeline failed", (), None
    )
    record.SourceId = "d0d0b245-a36e-4367-a5c4-c577253b4e10"
    record.Pipeline = "entities"
    record.private_text = "PRIVATE_INPUT_NOT_A_LOG_FIELD"
    record.exc_text = "PRIVATE_PARSER_DETAIL"
    rendered = StructuredFormatter().format(record)
    body = json.loads(rendered)
    assert datetime.fromisoformat(body["Timestamp"]).utcoffset().total_seconds() == 0
    assert body["State"] == {"SourceId": record.SourceId, "Pipeline": "entities"}
    assert "PRIVATE_" not in rendered


def test_repeated_app_creation_does_not_duplicate_handlers():
    first = application_logger()
    second = application_logger()
    assert first is second
    assert (
        sum(isinstance(handler.formatter, StructuredFormatter) for handler in first.handlers) == 1
    )
