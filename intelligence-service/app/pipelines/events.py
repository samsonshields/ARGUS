import re
from bisect import bisect_right
from datetime import date

from spacy.language import Language

from app.models.events import EventCandidate
from app.pipelines.entities import ExtractionLimitError

MONTHS = {
    name.lower(): number
    for number, name in enumerate(
        [
            "January",
            "February",
            "March",
            "April",
            "May",
            "June",
            "July",
            "August",
            "September",
            "October",
            "November",
            "December",
        ],
        1,
    )
}
MONTH_PATTERN = "(?:" + "|".join(MONTHS) + ")"
PATTERN = re.compile(
    rf"\b(?:\d{{4}}-\d{{2}}-\d{{2}}|\d{{4}}-\d{{2}}|"
    rf"\d{{1,2}}/\d{{1,2}}/\d{{4}}|"
    rf"{MONTH_PATTERN}\s+(?:\d{{4}}|\d{{1,2}}(?:st|nd|rd|th)?(?:,?\s+\d{{4}})?)|"
    rf"\d{{1,2}}\s+{MONTH_PATTERN}\s+\d{{4}}|"
    r"(?:last|next|this)\s+(?:Monday|Tuesday|Wednesday|Thursday|Friday|Saturday|Sunday|week|month|year)|"
    r"today|yesterday|tomorrow|\d{4})\b",
    re.IGNORECASE,
)


def interpret(value: str) -> tuple[str, str | None, str]:
    """Only explicit absolute dates; never infer a missing year, locale or reference date."""
    cleaned = re.sub(r"(\d)(st|nd|rd|th)\b", r"\1", value, flags=re.IGNORECASE)
    try:
        if re.fullmatch(r"\d{4}-\d{2}-\d{2}", cleaned):
            return "EXACT", date.fromisoformat(cleaned).isoformat(), "Explicit ISO calendar date."
        if re.fullmatch(r"\d{4}-\d{2}", cleaned):
            date.fromisoformat(cleaned + "-01")
            return "MONTH", cleaned, "Only a month is specified; no exact day is asserted."
        if re.fullmatch(r"\d{4}", cleaned):
            date(int(cleaned), 1, 1)
            return "YEAR", cleaned, "Only a year is specified; no exact month/day is asserted."
        full = re.fullmatch(
            rf"({MONTH_PATTERN})\s+(\d{{1,2}}),?\s+(\d{{4}})", cleaned, re.IGNORECASE
        )
        reverse = re.fullmatch(
            rf"(\d{{1,2}})\s+({MONTH_PATTERN})\s+(\d{{4}})", cleaned, re.IGNORECASE
        )
        if full:
            parsed = date(int(full[3]), MONTHS[full[1].lower()], int(full[2]))
            return "EXACT", parsed.isoformat(), "Explicit English calendar date."
        if reverse:
            parsed = date(int(reverse[3]), MONTHS[reverse[2].lower()], int(reverse[1]))
            return "EXACT", parsed.isoformat(), "Explicit English calendar date."
        month = re.fullmatch(rf"({MONTH_PATTERN})\s+(\d{{4}})", cleaned, re.IGNORECASE)
        if month:
            parsed = date(int(month[2]), MONTHS[month[1].lower()], 1)
            return (
                "MONTH",
                f"{parsed.year:04d}-{parsed.month:02d}",
                "Only a month is specified; no exact day is asserted.",
            )
    except ValueError:
        return "UNKNOWN", None, "Invalid calendar date; analyst correction is required."
    return (
        "UNKNOWN",
        None,
        "Ambiguous, partial or relative expression; "
        "no year, locale or reference date was inferred.",
    )


def extract_events(nlp: Language, text: str, limit: int = 2000) -> list[EventCandidate]:
    utf16 = [0]
    for char in text:
        utf16.append(utf16[-1] + (2 if ord(char) > 0xFFFF else 1))
    matches = list(PATTERN.finditer(text))
    if len(matches) > limit:
        raise ExtractionLimitError()
    sentences = list(nlp(text).sents)
    sentence_starts = [sentence.start_char for sentence in sentences]
    result = []
    for match in matches:
        index = bisect_right(sentence_starts, match.start()) - 1
        sentence = sentences[index] if index >= 0 else None
        if (
            sentence is None
            or match.end() > sentence.end_char
            or utf16[sentence.end_char] - utf16[sentence.start_char] > 8000
        ):
            raise ExtractionLimitError()
        precision, value, reason = interpret(match[0])
        result.append(
            EventCandidate(
                dateText=match[0],
                start=utf16[match.start()],
                end=utf16[match.end()],
                sentenceStart=utf16[sentence.start_char],
                sentenceEnd=utf16[sentence.end_char],
                quote=sentence.text,
                precision=precision,
                dateValue=value,
                reason=reason,
            )
        )
    return result
