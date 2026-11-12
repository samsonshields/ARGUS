# Entity-processing JSON contract v1

The independent Python service never opens or writes SQLite or source files. It
receives exact extracted text over loopback HTTP and returns candidates only.
Only Phase 3 endpoints exist: GET `/health` and POST `/process/entities`.
FastAPI serves `/openapi.json` and `/docs`; relationships/events are not implemented.

Request: `contractVersion` = "1", `sourceId` = GUID string, `textHash` = lowercase
SHA-256 of UTF-8 exact text, and `text` = the unchanged extracted string. Unknown
request properties and mismatched hashes are rejected. Empty/whitespace text returns
an empty candidate list when a trained model is available, never fabricated entities.

Response echoes contractVersion/sourceId/textHash and adds:

```json
{
  "offsetConvention": "UTF16_CODE_UNITS",
  "model": {"name": "en_core_web_sm", "version": "3.8.0", "spacyVersion": "3.8.18"},
  "entities": [
    {"text": "Alice Morgan", "type": "PERSON", "start": 3, "end": 15, "confidence": null}
  ]
}
```

The illustrative candidate above indexes `😀 Alice Morgan`: Python Unicode indexes
are converted to **UTF-16 code-unit** offsets. Starts are inclusive; ends exclusive.
Offsets refer to exact input/snapshot text, without additional NLP normalization.
They do not index raw PDF bytes or the pre-normalized uploaded file.

Deterministic label mapping: PERSON -> PERSON; ORG -> ORGANIZATION; GPE/LOC/FAC ->
LOCATION; DATE/TIME -> DATE; EVENT -> EVENT; all other labels -> OTHER. DATE candidates
remain date mentions, not resolved timeline events. No relative dates are invented.
Duplicate span/type candidates are removed deterministically. Confidence is always null.

The API independently validates version, source, hash, model metadata, allowed types,
span bounds, surrogate boundaries, exact substring equality, null confidence, candidate
count and response byte limits before storing any proposed records. Invalid results
fail atomically. Snapshots are immutable, versioned by source and exact text hash.

Python limits: 12,100,000-byte request body, 15-second per-body-chunk receive deadline,
250,000 UTF-16 text units by default, 5,000 candidates and 2,000 characters per mention.
Inference is serialized; busy service returns 503. There is no silent truncation.
.NET defaults: 30-second deadline covers HTTP headers **and body**, a 4 MiB response cap,
5,000 candidates and matching 250,000-character text limit.

Error responses are application/problem+json with status/code/title. Codes include
invalid_request, model_unavailable, service_busy, text_limit, candidate_limit,
body_too_large, body_timeout and pipeline_failed. Validation errors include field/type
identifiers but omit submitted input. Logs omit document contents and parser diagnostics.
Missing/incompatible/non-English models yield 503 readiness; no blank/rule/remote fallback
is used. Health 200 reports the actual loaded model version; health 503 is not readiness.

Python inference already in progress can finish after an HTTP timeout/cancellation.
.NET bounds retries and discards results from cancelled jobs. This service is intended
for a single local API instance and must remain bound to loopback.
