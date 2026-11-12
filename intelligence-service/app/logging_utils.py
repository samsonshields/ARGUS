"""Structured application diagnostics without submitted text or parser exception details."""

import json
import logging
from datetime import datetime, timezone


class StructuredFormatter(logging.Formatter):
    def format(self, record: logging.LogRecord) -> str:
        state = {
            key: getattr(record, key) for key in ("SourceId", "Pipeline") if hasattr(record, key)
        }
        return json.dumps(
            {
                "Timestamp": datetime.now(timezone.utc).isoformat(timespec="milliseconds"),
                "LogLevel": record.levelname,
                "Category": record.name,
                "Message": record.getMessage(),
                "State": state,
            }
        )


def application_logger() -> logging.Logger:
    logger = logging.getLogger("argus.intelligence")
    if not any(isinstance(handler.formatter, StructuredFormatter) for handler in logger.handlers):
        handler = logging.StreamHandler()
        handler.setFormatter(StructuredFormatter())
        logger.addHandler(handler)
    return logger
