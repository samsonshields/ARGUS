# Entity-processing JSON contract v1

The independent Python service never opens or writes SQLite or source files. It
receives exact extracted text over loopback HTTP and returns candidates only.
Phase 3 endpoints are GET `/health` and POST `/process/entities`.
Phase 4 adds POST `/process/relationships` as described in [graph contracts](graph-contracts.md).
Phase 5 adds POST `/process/events`; see [temporal contracts](temporal-contracts.md).
FastAPI serves `/openapi.json` and `/docs`.

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
LOCATION; DATE/TIME -> DATE; EVENT -> EVENT. CARDINAL, ORDINAL, QUANTITY, MONEY and
PERCENT are excluded: standalone counts, rankings, measurements and amounts are not
named entities. Other labels (including NORP) -> OTHER. DATE candidates
remain date mentions, not resolved timeline events. No relative dates are invented.
Duplicate span/type candidates are removed deterministically. Confidence is always null.
Each repeated occurrence retains its own exact source span. A candidate count is not
a count of unique identities, nor an assertion that every word in a source is an entity.
Existing stored candidates and approved/rejected history are retained when reprocessing;
the quantity filter affects newly generated candidates and does not erase prior findings.

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

## Phase 5 date candidates

`POST /process/events` uses the same version 1 hashed-text request and UTF-16 convention.
It returns deterministic date mentions and enclosing sentences for analyst review,
with exact/month/year/unknown precision and null confidence. See
[temporal contracts](temporal-contracts.md) for response fields, supported formats and limits.
