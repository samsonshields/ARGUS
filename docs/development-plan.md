# ARGUS development plan

Current scope: Phases 1–4. Phase 5 has not begun.

## Phase 1 implementation

- [x] Inspect repository/specification and verify .NET/Python toolchains.
- [x] Initialize Argus.sln with Web, API, Core and Infrastructure projects.
- [x] Implement investigation model, domain validation, DTOs and application service.
- [x] Configure SQLite/EF Core; generate and apply initial migration.
- [x] Implement CRUD, search/status filters, archive/unarchive and confirmed deletion.
- [x] Build dark dashboard, investigation list/create/edit/overview pages.
- [x] Handle loading, empty/error states, validation and save feedback.
- [x] Add unit and isolated SQLite/API integration tests, including restart persistence.
- [x] Document setup, API contracts, architecture and schema.
- [x] Record final formatting/build/test/live smoke verification.

## Phase 1 verification record

Toolchains: .NET SDK 10.0.102, runtime 10.0.2, Python 3.14.6.
Final verification on 2026-11-11:

- `dotnet build Argus.sln --no-restore`: passed, zero warnings/errors.
- `dotnet test Argus.sln --no-build --no-restore`: 10 unit and 11 integration tests passed; zero failures/skips.
- `dotnet format Argus.sln --no-restore --verify-no-changes`: passed.
- Initial EF migration generated and applied successfully to the local database.
- `dotnet ef migrations has-pending-model-changes`: no model drift.
- `dotnet list Argus.sln package --vulnerable --include-transitive`: no known vulnerable packages reported.
- `python scripts/smoke.py`: passed API/Web launch, rendered dashboard/list/detail/create/edit pages,
  CSS, CRUD and persistence after a real API process restart; temporary servers stopped.

Phase 1 implementation and automated completion gate passed. Browser interaction and
visual review remain a documented verification limit, not a completed browser test.

## Known limits

Browser-driven interactive forms, Blazor circuits and visual inspection require a manual
UI pass; live HTTP smoke checks cover rendered pages and server startup. No Python
service/tests apply yet. No authentication/public hosting, conflict detection or
pagination; lists target a small local workload. SQLite case folding is ASCII-oriented.

## Phase 2 implementation

- [x] Re-read specification/current architecture and verify SDK/Python toolchains.
- [x] Verify/install compatible PdfPig 0.1.16 dependency.
- [x] Add source records, metadata validation, manual A-E reliability and audit history.
- [x] Generate SourceDocuments migration, with unique hashes and restricted foreign keys.
- [x] Preserve managed originals outside static assets; use generated storage keys and sanitized display names.
- [x] Support .txt/.pdf multipart uploads and directly pasted text.
- [x] Enforce configurable byte/page/text/deadline limits and safe unsupported/malformed-file handling.
- [x] Extract strict Unicode text/PDF text; normalize text and retain extraction status/errors.
- [x] Compute SHA-256 from original bytes and detect duplicates per investigation, including concurrent imports.
- [x] Add source library, title/content/publication-date search, source viewer, original downloads and metadata editing.
- [x] Retry text extraction without deleting originals or prior successful text on failure.
- [x] Show actual source/extraction totals on dashboard and preserve investigation navigation context.
- [x] Add fictional text/PDF fixtures and automated domain/storage/API tests.
- [x] Verify Phase 1 migration upgrades without loss; verify originals/records survive API restart.
- [x] Update setup, API contracts, schema and architecture documentation.
- [x] Record final formatting/build/test/audit/smoke results.

## Phase 2 final verification

Toolchains unchanged: SDK 10.0.102, runtime 10.0.2, Python 3.14.6.
Release configuration is used because existing Debug API/Web processes hold their binaries open.
Completed on 2026-11-11 (America/New_York):

- `dotnet build Argus.sln -c Release --no-restore`: passed, zero warnings/errors.
- `dotnet test Argus.sln -c Release --no-build --no-restore`: **55 passed** (28 unit, 27 integration), zero failures/skips.
- `dotnet format Argus.sln --no-restore --verify-no-changes`: passed.
- `dotnet ef migrations has-pending-model-changes ... --configuration Release --no-build`: no model drift.
- `dotnet list Argus.sln package --vulnerable --include-transitive`: no known vulnerable packages reported.
- `python scripts/smoke.py` with `ARGUS_BUILD_CONFIGURATION=Release`: passed API/Web launch,
  dashboard/investigation/source routes and CSS; text/PDF/large-Unicode-paste imports;
  hashes, duplicates, safe failures, publication-date/content search, escaped viewer,
  original downloads through API/Web, extraction retry, deletion restrictions and restart persistence.
- `python -m py_compile scripts/smoke.py`: passed. No Python service tests apply.
- Phase 1 database upgrade to SourceDocuments passed without losing the existing investigation.
- Default database remained free of test rows after the corrected isolated runs.

Phase 2 automated completion gate passed. Temporary verification servers were stopped;
existing Debug application instances were left running. Restart/rebuild them to use Phase 2.

The first Phase 2 integration run found eager data-path resolution ignored test-host
configuration. This was fixed by resolving SQLite/document paths through DI from final
configuration. An explicit path assertion prevents regression. Identified synthetic
artifacts were removed from the default database after a complete backup to ignored
`.local/failed-test-run-backup`; no pre-existing investigation rows were present.

## Phase 2 known limits

No OCR, URL fetching, PDF password entry or Python/NLP service. PDF text/layout may vary.
Extraction is synchronous; no persistent job queue exists. Limits bound local parsing
but the child process is not a security sandbox. A hard crash between file creation
and DB commit can leave an orphan file; no automatic cleanup deletes potential evidence.
Keep database and documents together when backing up with the API stopped.
Browser clicks/file selection and visual review remain manual checks; HTTP smoke
verifies server-rendered routes and API behavior, not interactive Blazor circuits.

Phase 3 was subsequently authorized and implemented below. Phase 4 remains untouched.

## Phase 3 implementation

- [x] Inspect current architecture and verify .NET 10.0.102 / Python 3.14.6.
- [x] Verify compatible Python wheels, install isolated venv, spaCy/FastAPI and trained English model.
- [x] Define versioned JSON contract with exact UTF-16 spans, SHA-256 text hashes and nullable confidence.
- [x] Implement stateless entity extraction, deterministic label mapping, readiness/errors and request/result limits.
- [x] Add typed .NET HTTP client with bounded full-response timeout and independent contract validation.
- [x] Add persistent jobs and hosted worker; queue imports, extract asynchronously, retry, cancel and recover after restart.
- [x] Add EntityProcessing migration with snapshots, entities, mentions, constrained links and active-job uniqueness.
- [x] Retain immutable evidence and review decisions across reprocessing/extraction changes.
- [x] Implement proposed/approved/rejected review, canonical corrections, explicit association and confirmed merges.
- [x] Implement deterministic normalized/alias/fuzzy suggestions without automatic person merges.
- [x] Build source/investigation review UI, highlighted evidence, entity pages and polling job monitor.
- [x] Add paginated Phase 3 APIs, dashboard counts and .NET/Python OpenAPI documentation.
- [x] Test domain invariants, API review/jobs, cancellation/recovery, model failures and provenance.
- [x] Verify real three-process offline NLP/review/outage/retry/restart workflow on temporary data.
- [x] Update setup, environment examples, contracts, architecture and schema documentation.
- [x] Record final build/test/format/lint/migration/dependency/smoke results.

## Phase 3 final verification

Verified stack: .NET SDK 10.0.102/runtime 10.0.2; Python 3.14.6; spaCy 3.8.18;
en_core_web_sm 3.8.0; FastAPI 0.162.4; Pydantic 2.14.3; Uvicorn 0.54.0.
Completed on 2026-11-11 (America/New_York):

- `dotnet build Argus.sln -c Release --no-restore`: passed, zero warnings/errors.
- `dotnet test Argus.sln -c Release --no-build --no-restore`: **86 passed** (42 unit, 44 integration), zero failures/skips.
- Python pytest: **28 passed**, including real-model extraction, exact Unicode spans, missing-model/invalid-request/limit/pipeline failures; one upstream test-client deprecation warning.
- Ruff lint and format checks passed for app, Python tests and both smoke scripts.
- `dotnet format Argus.sln --no-restore --verify-no-changes`: passed.
- EF migration drift check: no pending model changes; Phase 1/Phase 2 upgrade tests passed.
- NuGet transitive vulnerability audit: no known vulnerable packages reported.
- `pip check`: no broken requirements; complete tested Python/model environment captured in requirements-lock.txt.
- Phase 1/2 compatibility smoke: passed in explicit text-only/worker-disabled mode.
- Real Phase 3 smoke: passed three-process startup, automatic queue/extraction/NER,
  proposals, Unicode spans, approval/rejection, stable reprocessing, source/evidence pages,
  service outage/bounded failures, manual retry, durable queue restart and byte-identical originals.
- Python smoke script syntax checks passed. Temporary servers/data were cleaned up.

Phase 3 automated completion gate passed. Existing normal application instances were
not replaced; rebuild/restart them using the updated three-terminal README instructions.
Phase 4 was not started.

OpenAPI dependency restore initially detected a high-severity advisory in transitive
Microsoft.OpenApi 2.0.0; an explicit patched 2.7.5 reference resolved it. No warning
suppression was used. All testing uses explicit temporary data paths; normal Debug
instances/default local databases are not used by the verification workflow.

## Phase 3 known limits

- English statistical NER is imperfect; all machine findings require review and confidence is null.
- Cancellation discards late results but does not forcibly stop an already running Python inference.
- Default NLP input is limited to 250,000 UTF-16 units; over-limit sources fail without truncation.
- One local worker/API instance per SQLite database; source/investigation list arrays remain unpaginated for compatibility.
- Browser clicks/file selection and visual appearance remain manually verified tasks, not completed browser tests.
- Python tests emit one upstream Starlette/httpx deprecation warning; it does not affect passing tests/runtime.
- Existing local security, no-OCR and potential crash-orphan limitations remain.

Phase 4 was subsequently authorized and implemented below; Phase 5 remains untouched.

## Phase 4 implementation

- [x] Inspect specification, architecture and verified .NET/Python toolchains.
- [x] Add relationship/evidence domain models, validation, audit and EntityGraph migration.
- [x] Add deterministic same-sentence Python co-mention generation over approved mentions.
- [x] Extend persistent worker with RELATIONSHIPS jobs, retry/cancel/recovery and atomic result persistence.
- [x] Preserve exact snapshot citations, stable rerun deduplication and independent passage review status.
- [x] Add explicit analyst relationship approval/rejection and substantive reasoning requirements.
- [x] Keep merge history/citations; retire self-links and preserve parallel relationships.
- [x] Implement bounded graph APIs, structural metrics, filtered shortest paths and incident-edge APIs.
- [x] Vendor verified Cytoscape 3.33.4 and license for offline graph rendering.
- [x] Implement graph pan/zoom, selection, filters, neighbor expansion, path highlighting and citations.
- [x] Add accessible HTML selectors and distinct proposed/co-mention/substantive edge styles.
- [x] Add domain/API/Python and 100-node/300-edge performance tests.
- [x] Run real headless Edge interactions and inspect captured screenshot.
- [x] Update README, architecture, schema, JSON/API contracts and environment requirements.
- [x] Record final build/test/lint/migration/audit/browser-smoke results.

## Phase 4 final verification

Toolchains unchanged: SDK 10.0.102/runtime 10.0.2; Python 3.14.6 and installed spaCy stack.
Verified Cytoscape 3.33.4 npm integrity and preserved MIT license; Playwright 1.65.1
uses installed Microsoft Edge without downloading a browser.
Completed on 2026-11-11 (America/New_York):

- Final sequential `dotnet build Argus.sln -c Release --no-restore`: passed, zero warnings/errors.
- `dotnet test Argus.sln -c Release --no-build --no-restore`: **101 passed** (47 unit, 54 integration), zero failures/skips.
- Python pytest: **38 passed**; one existing upstream Starlette/httpx test-client deprecation warning.
- .NET formatting verification, Ruff lint/format and Python/JavaScript syntax checks: passed.
- EF migration consistency: no model drift. Phase 3 job history upgraded with ENTITIES as the default kind.
- NuGet transitive vulnerability audit: no known vulnerable packages reported; `pip check`: no broken requirements.
- Earlier-phase API/Web and real NLP smoke scripts: passed, including restart persistence.
- Real headless Edge graph smoke: passed **100 nodes / 300 edges**; latest render **207 ms**,
  initial load **952 ms** on this development machine (not a universal performance guarantee).
- Browser coverage: actual canvas-node click, keyboard selectors, pan/zoom, filters,
  neighbor expansion, shortest path, quoted citations, co-mention/substantive evidence
  approval, blocked non-loopback assets, XSS-safe labels, deduplicated reruns and restart persistence.
- Unit graph metric benchmark also passed its 3-second 100-node/300-edge budget.
- Captured graph screenshot inspected: `.local/phase4-graph-100-300.png`; reviewed view:
  `.local/phase4-graph.png`. Temporary servers and test data were cleaned up.
- Independent new-passage review is covered: additional citations on a reviewed co-mention
  remain proposed until explicitly approved; existing reviews are never silently promoted/reset.

A final build initially overlapped with an owned smoke server and hit a Windows Web DLL
lock; after the smoke servers stopped, the sequential rebuild passed without warnings.
Normal application instances/default databases were not used for tests. Phase 4 automated
completion gate passed; restart API, Web and Python to load the new implementation.

## Phase 4 limits

- Metrics describe structure, not truth, importance, intent or threat.
- English statistical NER and sentence splitting can be imperfect; all candidates need review.
- Graphs are capped at 2000 nodes/10000 edges; larger responses require filtering.
- One worker/API per SQLite database; generation and HTTP limits are explicit in graph-contracts.md.
- Browser verification targets Edge desktop graph workflows, not every browser/device or earlier upload workflow.
- Existing no-authentication/no-OCR/crash-orphan constraints and one upstream Python test-client warning remain.

Next: await authorization for Phase 5. No timelines, event/date-resolution pipelines,
geographic locations, maps, Leaflet integration or Phase 5 APIs were started.
