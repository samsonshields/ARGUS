# Phase 5: temporal and geographic contracts

These APIs extend `/api/v1`. Web uses `TemporalApiClient`; only the API writes SQLite.
They add no Phase 6 assessments, claims, confidence scoring or reports.

## Date extraction

`POST /sources/{id}/process-events` queues a persistent `EVENTS` job and returns 202
with its job resource. The existing monitor, cancel, retry and restart recovery APIs
apply. Source text must already have been extracted. Events do not alter source text
or its extraction status. There is still only one active job per source, across all kinds.

The API calls Python `POST /process/events` with the same version 1 sourceId, textHash
and text request as entity processing. The response contains contractVersion, sourceId,
textHash, offsetConvention `UTF16_CODE_UNITS`, method `date_mentions_v1` and events:

```json
{
  "dateText": "March 2026",
  "start": 10,
  "end": 20,
  "sentenceStart": 0,
  "sentenceEnd": 35,
  "quote": "Exact enclosing source sentence here",
  "precision": "MONTH",
  "dateValue": "2026-03",
  "reason": "Only a month is specified; no exact day is asserted.",
  "confidence": null
}
```

Offsets are inclusive/exclusive into the exact hashed snapshot; this illustration is
not a valid request fixture. Both date text and the entire quote are independently
validated by .NET, including surrogate boundaries and consistency of absolute date values.
No calibrated probability is available. Detection is deterministic English/ISO date
mention extraction plus spaCy sentence boundaries; it does not establish an event occurred.
The sentence pipeline works without trained NER weights; the shared `/health` endpoint
still reports NER readiness, and entity extraction requires the installed model.

Supported explicit dates: ISO day/month/year, full English month/day/year, day/month/year
with a spelled-out English month, and English month/year. Invalid calendar dates,
numeric slash dates, missing years and relative expressions such as last Tuesday remain
UNKNOWN. No current date, publication date, numeric date locale or missing year is inferred.
Analysts can correct values manually after reading the source.

Limits: 250,000 UTF-16 units by default (existing configurable NLP input limit),
2,000 date candidates, 8,000 UTF-16 units per sentence/citation, 4 MiB HTTP response,
existing full-response timeout and bounded retry settings. Exceeding a limit fails
without truncating the document or committing partial results.

Validated proposals, their citations, job completion and audit events commit together;
no transaction remains open during HTTP. Late cancelled results are discarded.
Origin keys hash snapshot + method + original date span. Reruns preserve corrected,
approved, rejected and removed records. Changed source text can produce new proposals;
old citations retain their original immutable snapshots.

## Events and timeline

| Method/path | Behavior |
| --- | --- |
| GET `/investigations/{id}/events` | Paginated events; approved by default |
| GET `/investigations/{id}/timeline` | Alias of the events query |
| POST `/investigations/{id}/events` | Create a manual proposal |
| GET/PUT `/events/{id}` | Read/correct a record |
| POST `/events/{id}/approve`, `/reject`, `/remove` | Explicit review or soft removal |
| POST `/events/{id}/evidence` | Add/reactivate an exact snapshot citation |
| DELETE `/events/{id}/evidence/{evidenceId}?revision=...` | Retire citation; retain history |
| GET `/sources/{id}/passages?page=1` | Snapshot-backed passage picker |

Filters: status PROPOSED/APPROVED/REJECTED; `allStatuses=true` overrides status;
type OTHER/MEETING/MOVEMENT/PUBLICATION/CUSTOM; optional ISO `from`/`to` dates;
`includeUnknown=true` by default; page >= 1, pageSize 1-100 (default 50).
Known dates sort by their earliest possible date, then title/ID; unknowns sort last.
Partial-date intervals overlap a filter if any possible date overlaps it.
When includeUnknown is true, unknown dates survive date filters explicitly.

Event edit: title (1-200), description (<=5000), datePrecision, dateValue,
optional endDateValue, type, optional locationId, entityIds (<=100), revision.
EXACT accepts yyyy-MM-dd and an optional end on/after start; MONTH accepts yyyy-MM;
YEAR accepts yyyy; UNKNOWN requires empty dates. Month/year bounds support sorting
and filtering only; UI labels always retain precision and never display those bounds
as asserted exact event dates. Time-of-day/timezone resolution is outside this phase.
locationId must identify an approved active profile in this investigation. Linked
entities must be active canonical entities in the same investigation.

Review/removal body: `{ "confirm": true, "revision": 3 }`. Approval requires at least
one active citation. Citation body: `{ "snapshotId": "GUID", "start": 0, "end": 80,
"revision": 3 }`. Up to 100 distinct citations are retained per record. Citation changes
and event corrections reset status to PROPOSED. Clients reload the new revision before
review. Stale revisions return 409; validation returns 400; missing/removed records 404.
Explicit removal is soft: the event, provenance, citations and audit history remain.

The passage picker preserves the current successfully extracted text in a snapshot
if needed and returns 10 chunks of <=2000 UTF-16 units per page, without splitting
surrogates. Citation APIs also accept precise subranges up to 8000 units. Responses
include sourceId/title, snapshotId/hash, offsets and the exact reconstructed quote.
Removing a citation retires its association without deleting its snapshot or source.

## Locations and map

| Method/path | Behavior |
| --- | --- |
| GET/POST `/investigations/{id}/locations` | List profiles / create proposal |
| GET/PUT `/locations/{id}` | Read/correct a profile |
| POST `/locations/{id}/approve`, `/reject`, `/remove` | Explicit review / soft retirement |
| POST `/locations/{id}/evidence` | Add/reactivate citation (same contract as events) |
| DELETE `/locations/{id}/evidence/{evidenceId}?revision=...` | Retire citation |
| GET `/investigations/{id}/map` | Approved known locations, associated approved events, saved focus |
| PUT `/investigations/{id}/map/focus` | Save viewport latitude, longitude and zoom (1-18) |

Location edit: entityId (approved canonical LOCATION), precision, latitude [-90,90],
longitude [-180,180], uncertaintyMeters, basis, notes (<=5000), confirmCoordinates,
revision. One active profile per entity. UNKNOWN requires empty coordinates and radius;
APPROXIMATE requires a positive radius <=20,000,000 metres; EXACT permits no radius
or zero. Known coordinates require explicit confirmation and explanatory notes.
basis is EXPLICIT_SELECTION or VERIFIED_SOURCE_METADATA, an analyst assertion that
must be checked against the citations. There is no geocoding or automatic coordinate
inference. Explicit point selections are identified as selections, not verified metadata.
Approval requires a source citation even for explicitly selected coordinates.
Edits/citation changes reset review status. UNKNOWN profiles can be reviewed, but are
never plotted. Retired/proposed/rejected locations and their event markers are excluded.

Map accepts the same type/date/includeUnknown filters for event markers. Location
entities remain visible independently of event date/type filters. List/map queries
bound active locations to 2000 per investigation and event markers to 2000 per map
response; an overflow returns 422 rather than truncation. Timeline remains paginated.
The saved focus is a viewport preference, not an evidence-backed geographic assertion.
It survives API restart. Panning/zooming/selection never changes evidence. A map click
copies coordinates into the editor draft only; saving and approval are separate actions.

Entity merges redirect event associations without duplicates and preserve profile IDs,
event location references and citations. Merges with two active location profiles return
409 until an analyst retires one. LOCATION type corrections require retiring its active
profile first. Retiring a profile hides its markers without deleting linked event facts.

Leaflet 1.9.4, images and license are local vendored assets with recorded npm integrity.
The default coordinate grid needs no external requests. Users may opt into standard
OpenStreetMap tiles; browser defaults retain caching, User-Agent and Referer, visible
attribution is provided, and there is no proxy, prefetch, bulk tile download or offline
tile cache. Tile failure leaves markers/grid available. Policies were checked against
the [OSMF tile usage policy](https://operations.osmfoundation.org/policies/tiles/).
Leaflet's [stable 1.9.4 reference](https://leafletjs.com/reference-1.9.4.html) documents
the map, circle, grid and tile APIs used. Online basemap success is not part of the
offline automated gate; the browser smoke deliberately blocks external tile requests.

No claim, assessment, report-export or Phase 6 data models/endpoints are introduced.
