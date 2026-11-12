# API contracts (Phases 1–4)

Base: http://localhost:5100/api/v1. JSON uses camelCase, string statuses Active/Archived,
GUID IDs, ISO UTC timestamps and YYYY-MM-DD scope dates.

| Method | Route | Result |
| --- | --- | --- |
| GET | /investigations?search=term&status=Active | 200 array; filters optional |
| POST | /investigations | 201 object and Location header |
| GET | /investigations/{id} | 200 object or 404 |
| PUT | /investigations/{id} | 200 replacement metadata or 404 |
| DELETE | /investigations/{id}?confirm=true | 204; 400 without confirmation; 404 missing; 409 if sources exist |

Request example:

```json
{"title":"Meridian (fictional)","description":"Synthetic research","status":"Active","startDate":"2026-01-01","endDate":null,"region":"Atlantic"}
```

Title is required, non-whitespace and at most 200 characters. Description defaults to
empty, max 5000; explicit null rejected. Optional region max 200; blank becomes null.
Text is trimmed on save. Dates nullable; end cannot precede start when both known.
Unknown statuses and integer statuses in request bodies are rejected.
Responses add server-owned id, createdAt and updatedAt to the request fields.
PUT preserves identity/creation time, replaces metadata and updates modification time.
Archive/unarchive via PUT status. Titles need not be unique.

Search matches title, description and region using SQLite lowercasing (ASCII casing),
max 200 characters. Lists are unpaginated and sorted updated descending, then ID.
Errors return application/problem+json: status/title/type; validation errors also
include an errors dictionary. Invalid requests return 400, missing resources 404,
unexpected failures generic 500. Standard ASP.NET routing/media-type status handling applies.
GET /health (outside versioned base) returns {"status":"healthy"} after migration
startup; it is liveness, not a continuous database probe. No later-phase endpoints exist.

## Sources

All routes below use the same `/api/v1` base. No NLP `/process` endpoint is implemented.

| Method | Route | Result |
| --- | --- | --- |
| GET | /import-settings | `{ "maxFileBytes": 10485760 }` (configured value) |
| GET | /source-statistics | `{ "total": 0, "ready": 0, "failed": 0 }` |
| GET | /investigations/{id}/sources?search=term&from=2026-01-01&to=2026-01-31 | 200 source summary array, newest retrieved first; 404 missing parent |
| POST | /investigations/{id}/sources | multipart upload; 201 preserved source with Location and a durable queued job |
| POST | /investigations/{id}/sources/text | pasted-text JSON; 201 preserved source with Location and a durable queued job |
| GET | /sources/{id} | 200 detailed source or 404 |
| PUT | /sources/{id} | replacement metadata; 200 detailed source or 404 |
| POST | /sources/{id}/extract-text | synchronous extraction retry; 200 detailed source or 404 |
| GET | /sources/{id}/original | byte-identical attachment, application/octet-stream, nosniff; 404 if missing |

Multipart fields: `File` (required), `Title` (required, 1-200 non-whitespace characters),
`Publisher` (optional, max 200), `OriginalUrl` (optional absolute HTTP(S) URL, max 2048,
stored only), `PublishedAt` (optional YYYY-MM-DD date), `Reliability` (A/B/C/D/E, default E).
Metadata text is trimmed; blank optional strings become null. MIME headers are untrusted;
only .txt/.pdf extensions are accepted. In the default Phase 3 mode, extraction runs
after acceptance in the background job and byte-format errors are recorded there.
Unsupported types return 415, empty files 400, oversize files 413. Very large HTTP/form
bodies are also bounded by server limits; ASP.NET binding failures can return 400.

Pasted-text JSON accepts the same camelCase metadata fields plus required `text`
(max 2,000,000 characters). It is saved as UTF-8 without BOM in a generated .txt file.
PUT accepts only metadata; original bytes, hash, type and retrieval time are immutable.

Detail response shape:

```json
{
  "source": {
    "id": "00000000-0000-0000-0000-000000000001",
    "investigationId": "00000000-0000-0000-0000-000000000002",
    "title": "Fictional report",
    "sourceType": "Text",
    "publisher": null,
    "publishedAt": null,
    "retrievedAt": "2026-11-11T20:00:00Z",
    "reliability": "E",
    "processingStatus": "Ready",
    "processingError": null,
    "originalFileName": "report.txt",
    "fileSize": 12
  },
  "originalUrl": null,
  "contentHash": "<64 lowercase hexadecimal SHA-256 characters>",
  "extractedText": "Source text",
  "extractedAt": "2026-11-11T20:00:00Z",
  "extractionVersion": "local-text-v1/pdfpig-0.1.16"
}
```

Lists return only the nested `source` summary shape, without text/hash/private storage paths.
Search covers title and extracted text using SQLite ASCII case folding, max 200 characters.
Date bounds are inclusive **publication dates**; records with unknown publication dates
are excluded when a bound is set. Reversed/malformed date ranges return 400.

Exact duplicate bytes within an investigation return 409 Problem Details with
`existingSourceId`, regardless of filename/title. A database unique constraint also
guards concurrent uploads. Different investigations can each preserve their own copy.

Phase 3 default imports return `processingStatus: "Pending"`; the worker moves sources
through Processing to Ready/Failed. `ProcessingStatus` describes text extraction,
while the separate job describes NLP completion. Thus a source can be Ready with
a failed NLP job if its text was extracted but the NLP service was unavailable.
Source response shape is unchanged; discover queued jobs by sourceId below.

With `Intelligence:AutoProcessImports=false`, the compatibility text-only mode remains:
extraction failures create a 201 source with `processingStatus: "Failed"` and
a safe `processingError`; originals are retained for inspection. A failed retry returns
200 Failed, keeping prior successful extracted text/time/version. Failed retries do not
imply the prior text is current. PDFs may be encrypted, malformed, image-only or over
limits. OCR and remote fetching are unavailable.

Web exposes `/downloads/sources/{id}` as an attachment proxy to the API, with no SQLite
or local document access. Source import/metadata/retry audit events are committed with
their associated database changes; no public audit-editing API exists.

## Phase 3 processing and entity review

New collections return `{ "items": [...], "total": 0, "page": 1, "pageSize": 50 }`.
`page` accepts 1–1,000,000 and `pageSize` 1–100. Existing investigation/source list
array contracts remain unchanged for compatibility. Ordering is deterministic.

| Method | Route | Result |
| --- | --- | --- |
| GET | /intelligence/health | 200 ready or 503 NLP/model unavailable |
| GET | /intelligence/statistics | proposed, approvedEntities, pendingJobs, failedJobs counts |
| POST | /sources/{id}/process | 202 job + Location; 409 if a job is already active |
| GET | /processing-jobs?investigationId=&sourceId=&page=1&pageSize=50 | paged job history, newest first |
| GET | /processing-jobs/{id} | current persisted job |
| POST | /processing-jobs/{id}/cancel | 200 CANCELLED; 409 for terminal jobs |
| POST | /processing-jobs/{id}/retry | 202 new job for FAILED/CANCELLED; 409 otherwise |
| GET | /investigations/{id}/mentions?sourceId=&entityId=&status=PROPOSED&type=&search=&page=1&pageSize=50 | paged mentions, ordered source/start/id |
| GET | /mentions/{id}/evidence | mention + before/quote/after + immutable textHash |
| GET | /mentions/{id}/suggestions | up to 10 same-type name/alias matches; never automatically merged |
| POST | /mentions/{id}/approve | 200 approved entity; 409 if already reviewed |
| GET | /investigations/{id}/mentions/bulk-approval-preview?sourceId=&type=&search= | all matching proposed IDs/counts across pages; read-only |
| POST | /investigations/{id}/mentions/approve-bulk | confirmed atomic approval of previewed IDs |
| POST | /mentions/{id}/reject | 204; 409 if already reviewed |
| GET | /investigations/{id}/entities?search=&page=1&pageSize=50 | paged active approved canonical entities |
| GET | /entities/{id} | canonical record, aliases, approved mention count and optional mergedIntoId |
| PUT | /entities/{id} | corrected canonical name/type; old name retained as alias |
| POST | /entities/merge | confirmed same-investigation, same-type merge; 200 retained entity |

Job fields: id/sourceId/investigationId/state/attempts/errorCode/error/createdAt/updatedAt/
nextAttemptAt/snapshotId/candidateCount. States: PENDING, RUNNING, COMPLETED, FAILED,
CANCELLED. Active jobs are unique per source. Source originals and reviewed findings
remain intact on failures. Transient failures retry within the configured total attempt
budget; permanent extraction/contract/limit failures do not retry automatically.
Interrupted running jobs recover on startup with their attempt count preserved.

Mention fields: id/sourceId/sourceTitle/snapshotId/text/originalType/start/end/status/
entityId/modelName/modelVersion/confidence (always null). Types are PERSON, ORGANIZATION,
LOCATION, DATE, EVENT, OTHER; review statuses are PROPOSED, APPROVED, REJECTED.
Omitted/empty status returns all review states; optional type filters the original
model classification, and search matches literal mention text (trimmed, maximum 200
characters, SQLite ASCII case-insensitive). Filters combine with investigation/source/
entity scope before counts and pagination. Invalid enum values return 400.
GET evidence returns a highlighted passage context in the UI; no raw HTML is returned.
Spans index the immutable text snapshot, not a mutable current extraction.

Approve request: `{ "name": "Alice Morgan", "type": "PERSON", "entityId": null }`.
Name is required and at most 200 characters. Optional entityId explicitly associates
the mention with an active canonical record in the same investigation; name/type
must match that record. Otherwise a new entity is created even for identical names.
Original mention text/type/model provenance are never overwritten by corrections.
PUT entity accepts name/type only. Mentions keep their original evidence after renames.

Bulk preview uses the same original-type/text filters and optional source scope as the
review list, always limited to PROPOSED mentions. Source scope must belong to the route's
investigation. It returns `candidateIds`, `proposedCount`, `skippedCount` and
`distinctNameTypeCount` (case/whitespace-normalized name plus original type). Candidates
with blank names or names longer than 200 UTF-16 units are excluded from candidateIds
and counted as skipped; they need an individual canonical-name correction. Over 5000
matching proposals returns 413 with a request to narrow filters, never a partial preview.

Bulk approval JSON: `{ "candidateIds": ["<GUID>"], "confirm": true,
"groupIdenticalNames": false }`. IDs must be 1–5000 distinct, nonempty GUIDs within the
route investigation. Only these IDs are processed, so new proposals arriving after a
preview are not silently added. A missing/foreign ID returns 404; any already reviewed
candidate returns 409; invalid/unconfirmed input returns 400. Validation completes before
changes; approval, new entities and audit records commit together in one transaction.

Default behavior creates one new canonical entity per mention using its original text
and model type. Explicit grouping creates one new entity per normalized name/type within
this batch. It never matches aliases, reuses existing canonical entities or merges them.
Names alone do not establish identity; grouping requires the analyst's explicit choice.
Every mention retains its own text, snapshot, model metadata and UTF-16 span, with a UTC
review timestamp. Existing approvals/rejections are unchanged. Per-mention `BulkApproved`
audits share a batch GUID and a summary records counts and the grouping choice.
The response is `{ "batchId": "<GUID>", "approvedCount": 42, "createdEntityCount": 8 }`.
Replaying a completed batch returns 409 without creating duplicate entities.
Relationships still require explicit co-mention generation; bulk approval creates no edges.

Merge request: `{ "sourceId": "<entity GUID>", "targetId": "<entity GUID>", "confirm": true }`.
Source and target must be different, active, of the same type and investigation.
The former entity remains with mergedIntoId; its aliases and approved mentions move
to the target atomically. Opposite/cyclic or stale merges are guarded by optimistic
concurrency. Invalid confirmation returns 400; incompatible records return 409.

New validation responses include `code: "validation_failed"`; concurrency errors
include `code: "concurrent_update"`; domain errors have machine-readable request codes.
Job error codes include interrupted, cancelled, text_extraction_failed, nlp_unavailable,
nlp_timeout, nlp_not_ready, nlp_error, invalid_contract, response_limit, text_limit and
processing_failed. No document content is included in technical failures.

The `/sources/{id}/extract-text` text-only endpoint returns 409 while a job owns the
source. Use `/process` for asynchronous extraction plus NLP. Reprocessing identical
text reuses mentions by snapshot/span/type and keeps prior review decisions. Changed
text adds a new snapshot and candidates; older evidence remains available.

.NET publishes `/openapi/v1.json`. See [Python JSON contract](nlp-contracts.md) for
readiness, hashing, label mapping and UTF-16 span rules. Phase 4 adds the
[relationship/graph contracts](graph-contracts.md), a RELATIONSHIPS job kind and
proposedRelationships/approvedRelationships statistics. Job retry retains its kind;
relationship jobs do not re-extract or alter source text state. Phase 5 additions
are documented below.

## Phase 5 additions

See [temporal and geographic contracts](temporal-contracts.md) for event generation,
timeline/review/citations, location profiles, map filters and geographic focus APIs.
All earlier APIs remain compatible. Phase 6 additions follow below.

## Phase 6 additions

See [assessment/report contracts](assessment-contracts.md) for claims, evidence
classifications, analyst confidence/review, summaries, report validation/history and
Markdown attachments. Phase 7 backup export is documented below.

## Phase 7 additions

`POST /api/v1/backups/export` streams a consistent workspace ZIP with a checksum manifest.
The Web download proxy is GET `/downloads/backups`; the user-facing page is
`/workspace-backup`. Recovery is an offline CLI operation into a new directory, with no
HTTP endpoint that overwrites a live database. See [quality/recovery contracts](quality-contracts.md).
Request completion diagnostics return `X-Argus-TraceId`; errors remain Problem Details.
Existing review/list contracts are compatible; batched queries preserve their data and
ordering. Multi-user/collaboration endpoints remain absent.
