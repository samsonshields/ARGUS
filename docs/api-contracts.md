# API contracts (Phases 1 and 2)

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
| POST | /investigations/{id}/sources | multipart upload; 201 detailed source with Location |
| POST | /investigations/{id}/sources/text | pasted-text JSON; 201 detailed source with Location |
| GET | /sources/{id} | 200 detailed source or 404 |
| PUT | /sources/{id} | replacement metadata; 200 detailed source or 404 |
| POST | /sources/{id}/extract-text | synchronous extraction retry; 200 detailed source or 404 |
| GET | /sources/{id}/original | byte-identical attachment, application/octet-stream, nosniff; 404 if missing |

Multipart fields: `File` (required), `Title` (required, 1-200 non-whitespace characters),
`Publisher` (optional, max 200), `OriginalUrl` (optional absolute HTTP(S) URL, max 2048,
stored only), `PublishedAt` (optional YYYY-MM-DD date), `Reliability` (A/B/C/D/E, default E).
Metadata text is trimmed; blank optional strings become null. MIME headers are untrusted;
only .txt/.pdf extensions are accepted and bytes must also pass text/PDF extraction.
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

Extraction failures still create a 201 source with `processingStatus: "Failed"` and
a safe `processingError`; originals are retained for inspection. A failed retry returns
200 Failed, keeping prior successful extracted text/time/version. Failed retries do not
imply the prior text is current. PDFs may be encrypted, malformed, image-only or over
limits. OCR and remote fetching are unavailable.

Web exposes `/downloads/sources/{id}` as an attachment proxy to the API, with no SQLite
or local document access. Source import/metadata/retry audit events are committed with
their associated database changes; no public audit-editing API exists.
