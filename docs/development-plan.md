# ARGUS development plan

Current scope: Phases 1-7 complete. Phase 7 completion gate passed.

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

Phase 5 was subsequently authorized and is recorded below.

## Phase 5 implementation

- [x] Re-read scope, inspect repository and verify .NET/Python toolchains.
- [x] Add event/evidence/entity association, location/evidence and geographic focus models.
- [x] Add additive TimelineGeography migration, constrained references and review concurrency.
- [x] Implement deterministic English/ISO date candidates with exact UTF-16 sentence citations.
- [x] Preserve unknown/relative/ambiguous dates; independently validate Python contracts in .NET.
- [x] Extend persistent jobs with EVENTS, bounded transport/retry/cancel/recovery and atomic results.
- [x] Implement manual events, corrections, explicit review, soft removal and stable reruns.
- [x] Implement source passage selection, immutable citation reconstruction and entity associations.
- [x] Implement paginated timeline, precision labels, date overlap and type/status filters.
- [x] Add explicitly selected/source-verified location profiles, review and uncertainty radii.
- [x] Preserve event/profile links on merges and reject unresolved conflicting profiles.
- [x] Vendor verified Leaflet 1.9.4, license, images and package integrity metadata.
- [x] Add offline grid, reviewed location/event markers, selection, citations and saved focus.
- [x] Add opt-in online OSM tiles with attribution, browser caching and offline failure fallback.
- [x] Add domain/API/Python tests including uncertain dates/places and Phase 4 migration upgrade.
- [x] Exercise real timeline/map workflows in headless Edge and inspect screenshots.
- [x] Update README, architecture, schema and temporal/API/NLP contract documentation.
- [x] Record final build/test/format/lint/migration/audit and regression smoke results.

## Phase 5 final verification

Verified on 2026-11-11 (America/New_York): .NET SDK 10.0.102/runtime 10.0.2,
Python 3.14.6, Node 24.18.1; existing installed spaCy/FastAPI/model stack unchanged.
Leaflet 1.9.4 npm tarball integrity was checked before vendoring; JS SHA-256 and the
original license are preserved in wwwroot/vendor/leaflet. OSM tile policy was checked.

- Final sequential `dotnet build Argus.sln -c Release --no-restore`: passed, zero warnings/errors.
- `dotnet test Argus.sln -c Release --no-build --no-restore`: **130 passed** (67 unit, 63 integration), zero failures/skips.
- Python pytest: **53 passed**, including date precision, ambiguous/relative/invalid dates,
  exact Unicode quotes, missing NER weights, limits and sanitized failures.
- Total automated unit/integration/Python tests: **183 passed**.
- .NET formatting verification: passed. Ruff lint/format, Python syntax and both application JavaScript syntax checks: passed.
- EF `has-pending-model-changes`: no model drift. Phase 4 migration upgrade preserves relationships/sources.
- First event job snapshot creation/cancellation, late result cancellation, retry/recovery,
  stale review prevention, cross-investigation rejection, merge collisions and removed-candidate reruns: passed.
- NuGet transitive vulnerability audit: no known vulnerable packages reported; `pip check`: no broken requirements.
- Earlier-phase live API/Web/source and real NLP outage/retry/restart smokes: passed.
- Phase 4 real Edge graph regression: 100 nodes / 300 edges; render 308 ms, initial load
  1412 ms on this machine. Existing graph performance and interactions remain passing.
- Phase 5 real three-process Edge smoke: passed timeline review, entity/source links,
  date correction without restart, precise/partial/unknown dates, offline Leaflet marker
  and uncertainty rendering, actual SVG marker click, keyboard selection, event filtering,
  draft-only map coordinate selection, location edits/reapproval, saved geographic focus,
  optional online tile failure/attribution, safe text labels, stable reruns and API restart persistence.
- Final screenshots inspected: `.local/phase5-timeline.png` and `.local/phase5-map.png`.
  Logs/screenshots/test data remain ignored. Temporary verification servers were stopped.
- Existing normal application instances/default databases were not used or replaced.

The initial verification caught and fixed an approved-only default obscuring the all-status
query, ASP.NET positional-record validation, an upstream OpenAPI nullable-enum default
serialization issue, circuit-readiness timing and accessible select names. The complete
final suite passes. No warning suppression or incomplete gate was used.

Phase 5 completion gate passed. Phase 6 was subsequently authorized and is recorded below.

## Phase 5 known limits and next steps

- Date detection is deliberately conservative English/ISO mention extraction, not semantic
  event verification. Relative dates, ambiguous numeric dates and missing years stay unknown;
  analysts can make explicit corrections. Time-of-day resolution is not implemented.
- Offline maps use a coordinate grid rather than cached geographic imagery. Optional online
  OSM imagery needs internet and exposes the viewport to the provider; automated checks block
  external requests and verify failure fallback/attribution, not successful imagery delivery.
- Timeline is paginated; map/location bounds are 2000 known/active profiles and 2000 event
  markers, with explicit overflow errors. One API/worker per SQLite database remains required.
- Removed finding/citation history remains in SQLite/audit records; removed findings have no
  restore/history browser workflow. Existing no-authentication/no-OCR/crash-orphan limits remain.
- One upstream Starlette/httpx test-client deprecation warning persists; runtime/tests pass.
- Restart API, Web and Python after building to load Phase 5 and apply the additive migration.
  Phase 6 was subsequently authorized; see its implementation and verification below.


## Phase 6 implementation

- [x] Read requirements, inspect architecture and verify .NET/Python toolchains.
- [x] Add claim/evidence/report models, GUIDs, UTC timestamps and additive migration.
- [x] Add manual confidence, reasoning, support/contradiction/context classifications.
- [x] Add exact snapshot evidence, source reliability, scope checks and optimistic review.
- [x] Preserve conflicts, require explicit unresolved acknowledgment and retain audit history.
- [x] Add claim editor, evidence controls, review filters and assessment summaries.
- [x] Add approved-only Markdown generation, ten sections, references and limitations.
- [x] Validate source/snapshot hashes, original files, exact spans and uncertain optional modules.
- [x] Persist immutable report snapshots and provide safe API/Web attachment downloads.
- [x] Add domain/API/migration/provenance tests and real CommonMark/browser verification.
- [x] Correct Unicode quotation reconstruction discovered by the report provenance gate.
- [x] Update README, architecture, schema and assessment/API contracts.
- [x] Complete final build/test/format/lint/migration/audit and live regression verification.

## Phase 6 final verification

Verified on 2026-11-11 (America/New_York): .NET SDK 10.0.102, runtime/ASP.NET 10.0.2,
Python 3.14.6 (including the existing venv), existing spaCy/FastAPI stack and Edge.
Markdown-it-py 4.3.0 was already installed/pinned and is now an explicit development
requirement for CommonMark verification. No production dependency or NLP endpoint was added.

- Final sequential `dotnet build Argus.sln -c Release --no-restore`: passed, zero warnings/errors.
- `dotnet test Argus.sln -c Release --no-build --no-restore`: **154 passed** (79 unit, 75 integration), zero failures/skips.
- Python pytest: **53 passed**, with the existing single upstream Starlette/httpx deprecation warning.
- Total automated unit/integration/Python tests: **207 passed**.
- .NET formatting verification, Ruff lint/format, Python compilation and application JavaScript syntax checks: passed.
- EF migration consistency: no model drift. Phase 5 upgrade retains existing temporal data; new claim/report tables start empty.
- NuGet transitive vulnerability audit: no known vulnerable packages reported; `pip check`: no broken requirements.
- New tests cover confidence/review invariants, unresolved acknowledgment, resolution resets,
  source grades, cross-investigation rejection, stale revisions, retained evidence/audit history,
  approved-only export, ten sections, empty optional modules, date/location uncertainty,
  hashes, missing/tampered originals, immutable snapshots, artifact tampering, export limits,
  UTF-16 passages after emoji, download bytes and API restart persistence.
- Phase 1/2 compatibility and real Phase 3 NLP/outage/retry/restart smokes: passed.
- Phase 4 real Edge graph regression: passed at 100 nodes/300 edges; render 391 ms,
  initial load 1949 ms on this machine, not a universal performance guarantee.
- Phase 5 real timeline/map Edge regression: passed, including optional tile failure,
  offline markers, precision, review, focus and restart persistence.
- Phase 6 API/Web Edge smoke: passed actual claim creation, three evidence relationships,
  manual confidence/source grades, unresolved acknowledgment, filters, evidence relabel/removal,
  report validation, finalized attachment download/hash, immutable snapshot history, correction
  withdrawal and restart persistence. All non-loopback requests were blocked.
- CommonMark rendering verification: passed all ten headings, valid P-number anchors,
  readable literal quotations and escaped HTML/JavaScript-link payloads.
- Screenshots inspected: `.local/phase6-assessments.png` and `.local/phase6-reports.png`.
  Verification servers and temporary data were cleaned up; normal instances/default data were not used.

The first provenance run caught an existing SQLite substring/UTF-16 mismatch on non-BMP
text. Quotation reconstruction now stays in .NET for claims, temporal evidence and report
entity passages. Regression tests cover exact passages after emoji. A browser test selector
was also scoped to distinguish the visible evidence label from its select option. Final
checks pass without suppressing warnings or bypassing report validation.

Phase 6 completion gate passed. Phase 7 was subsequently authorized below.

## Phase 6 known limits and next steps

- Confidence, source reliability and contradiction resolution are analyst judgments, not
  independently calculated truth. Reports explicitly retain these distinctions and uncertainty.
- Markdown attachments and an encoded text preview are implemented; PDF export and editable
  HTML reports are not. Reference source routes are local ARGUS routes; exported reports also
  retain IDs, exact quotations, offsets and hashes for provenance outside the running UI.
- Reports preserve review state at generation time. Later corrections require a new report;
  earlier snapshots are retained. Claims/evidence use soft removal with audit history and no restore UI.
- Export bounds are documented in assessment-contracts.md. Generation uses one bounded
  SQLite transaction; run one API/worker per database. These exports are not complete backups.
- Browser checks target installed Edge desktop; broader device/browser coverage remains unverified.
- The upstream Python test-client warning and existing no-authentication/no-OCR/crash-orphan
  limitations remain. No critical Phase 6 runtime issue is known from the executed checks.
- Restart API and Web to load the new implementation and apply AssessmentsReports.
  Phase 7 was subsequently authorized; its record follows.


## Phase 7 implementation

- [x] Re-read specification/architecture and verify SDK, runtimes and Python toolchains.
- [x] Refine dark workstation styling, context navigation, keyboard focus and mobile wrapping.
- [x] Add distinct actionable error/loading/empty states and safe network-error wording.
- [x] Add structured UTC request/job/NLP diagnostics without document/query/parser content.
- [x] Batch common claim/timeline/location queries and add the WorkspaceQuality indexes.
- [x] Enforce fixed query budgets with fifty-row integration fixtures.
- [x] Add the complete fictional Operation Meridian dataset and real-API training loader.
- [x] Add live workspace backup, checksum manifest and offline safe restore into a new directory.
- [x] Test malformed archives, traversal, duplicates, symlinks, checksums, original integrity and persistence.
- [x] Add a complete real-model/API/browser/backup/restore/restart demonstration smoke.
- [x] Capture/inspect desktop and mobile views; add fictional screenshots/example to README.
- [x] Add setup, walkthrough, design, quality/recovery and architecture/schema/API documentation.
- [x] Correct Git ignore rules that hid Infrastructure/Data source and migrations on Windows.
- [x] Complete source-only/fresh-environment verification and final regression checks.

## Phase 7 final verification

Verified on 2026-11-12 (America/New_York), using .NET SDK 10.0.102,
ASP.NET/runtime 10.0.2, Python 3.14.6 and installed Microsoft Edge on Windows x64.

- `scripts/verify.ps1 -Full`: passed the Release build, automated suites, formatting,
  lint, dependency consistency and all six isolated live workflows.
- Release build: zero warnings/errors. .NET tests: **164 passed** (79 unit,
  85 integration). Python tests: **58 passed**. Total: **222 passed**, zero failures/skips.
- Python retains one upstream Starlette/httpx test-client deprecation warning;
  it is documented and not suppressed.
- .NET format verification, Ruff lint/format and application JavaScript syntax checks:
  passed. EF reports no pending model changes; NuGet's transitive audit reports no
  known vulnerable packages; `pip check` reports no broken requirements.
- Live regressions passed for import/original preservation, real NLP/review/queue recovery,
  graph exploration, timeline/offline geography and claims/CommonMark reports.
- Graph regression with 100 nodes/300 edges: 225 ms render, 820 ms initial load on
  this machine. Fifty-row integration fixtures enforce bounded query counts for claims,
  timeline and locations; these timings are observations, not universal guarantees.
- The complete Meridian workflow passed real ingestion/model proposals, analyst corrections,
  merge history, 14 active entities, 18 edges (15 proposed/3 reviewed), ten dated events,
  five locations and five claims with two explicitly unresolved conflicts. It generated
  an immutable source-backed report and exercised desktop/mobile navigation.
- The browser downloaded a live workspace backup. Offline CLI restore, a real service
  restart and comparisons of original bytes/hashes, reviews and reports passed. Malformed
  archives, missing/tampered originals, traversal, duplicate entries, symlinks and checksum
  failures are covered by integration tests; existing restore destinations are rejected.
- Restored browser views passed; an intentional API outage produced an actionable error
  and reload control without an endless loading state. The 390-pixel layout had no
  horizontal page overflow. External network requests were blocked during offline workflows.
- After the final map animation refinement, both the temporal regression and full Meridian
  smoke passed again. The final Meridian run rendered its graph in 53 ms and completed
  in 28.7 seconds. The README's six fictional application screenshots were regenerated
  and inspected, alongside the mobile capture.
- `scripts/verify_clean_checkout.py --fresh-python` passed tool restore, package restore,
  Release build, all .NET tests, installation of the locked Python/model environment into
  a new venv, Python tests and dependency consistency. A subsequent source-only copy
  also passed. This checks the current publishable working tree, not a remote Git clone.
- Fixed broad `data/` ignore rules that hid Infrastructure/Data source and all migrations
  on Windows. Source-only verification explicitly requires the database context. Local
  databases, originals, environments, logs and secrets remain excluded.
- `git diff --check`: passed. Test services used isolated ports/temporary data and stopped
  afterward; existing application instances and normal workspace data were left untouched.

Phase 7 completion gate passed. All seven specified phases are implemented; no additional
phase or deployment work was started. Source changes remain available for review without
staging or committing.

## Phase 7 known limits and next steps

- Backup v1 is bounded to 5,000 originals and 512 MiB of database plus original data;
  individual originals are limited to 100 MiB. Archives are checksummed, not signed or
  encrypted. Restore trusted archives into a new directory and keep independent copies.
- Export preparation has a two-minute I/O deadline; SQLite's native synchronous snapshot
  cannot be forcibly interrupted by that deadline. A hard process crash can leave
  temporary export/orphan files. See quality-contracts.md for recovery and size details.
- Windows x64 and installed Edge desktop/mobile emulation were verified. Other operating
  systems, browser engines and physical mobile devices remain unverified.
- The existing single-user/loopback, no-authentication, no-OCR and English-model limitations
  remain. Findings, location profiles and confidence require analyst review; reports preserve
  their generation-time state rather than updating after later corrections.
- Restart the API, Web and NLP services to load the changes and apply WorkspaceQuality.
  Follow setup.md and demo-walkthrough.md for normal use, demonstration and recovery.
  No critical runtime issue is known from the executed checks.

## Post-Phase 7 interface redesign

Visual and interaction redesign of Argus.Web only; no API, Core, schema or migration change.
Second pass restyled the first redesign to a dark "command console" system (top navigation,
inset bands, stat cards, corner-bracket frames, IBM Plex Sans). See design.md.

- [x] Single token-based app.css; vendor IBM Plex Sans and Source Code Pro (OFL) locally.
- [x] Top navigation, footer, configurable marking strip (`Workstation:Marking`) and UTC clock.
- [x] Shared status, confidence and reliability components using one review-state language.
- [x] List/inspector layouts for relationships, timeline, geography and assessments; tables for libraries.
- [x] Align graph and map rendering colors with the legend and tokens; regenerate screenshots.

Verification (`scripts/verify.ps1 -Full`): zero build warnings/errors; 164 .NET tests
(79 unit, 85 integration) and 58 Python tests passed; dotnet format, ruff and pip check clean;
all six smoke suites passed, including the mobile no-overflow check and the
zero-external-request assertion. Accessible names, labels and smoke-test hooks were preserved.
Other browser engines and physical devices remain unverified.

## Post-Phase 7: candidate review and graph filter corrections

Completed on 2026-11-12 (America/New_York), following the local DNI 2026 threat-assessment
PDF report. Preserved the user's defense-focused UI: no CSS, fonts, colors, layout theme,
graph rendering styles or portfolio screenshots were changed by this correction.

- Review status and candidate-type selections now refresh immediately and reset pagination.
  Added an explicit all-status view and bounded text search. Approved and rejected mentions
  retain their original evidence and remain inspectable; approved canonical entities are
  still listed independently. Each repeated source occurrence remains a separate record.
- API mention filters combine status, original type, literal text search and existing
  investigation/source/entity scopes before pagination. Invalid enum values return 400.
- Review loads discard superseded responses, clamp pages after reviewing the last item,
  and show an error instead of perpetual loading when the initial request fails.
- The graph's proposed-edge checkbox applies immediately. Inline guidance explains that
  it controls relationships between approved canonical entities. Entity proposals do not
  become graph nodes before analyst review; relationship generation remains explicit.
- The spaCy entity pipeline excludes CARDINAL, ORDINAL, QUANTITY, MONEY and PERCENT
  results rather than converting ordinary counts/amounts into OTHER identities. Dates,
  names, nationalities and other retained model labels keep their existing mappings.
  No model/dependency replacement or database migration was required.
- Existing stored proposals and all review decisions are preserved. Rerunning extraction
  does not erase older numeric proposals. Use type/search filters to narrow existing queues;
  the new exclusion applies to newly generated candidates.
- Added `scripts/smoke_candidate_review.py`, included in the full verification gate (now
  seven smokes). Optional `--pdf <local-path>` imports the supplied file into temporary
  data, never the normal workspace. Updated setup and API/NLP contracts.

Verification:

- .NET SDK 10.0.102 and Python 3.14.6 verified. Debug build passed with zero warnings/errors.
  The initial Release build could not copy locked assemblies held by the user's existing
  API/Web processes; those processes were left running and validation used Debug instead.
- .NET: **168 passed** (79 unit, 89 integration); Python: **64 passed**. Total:
  **232 passed**, zero failures/skips. One upstream Starlette/httpx warning remains.
  An initial pytest cache warning was avoided using a fresh ignored test cache directory.
- .NET format verification, Ruff lint/format and `git diff --check`: passed.
- New real-model/headless Edge workflow passed paging beyond the first twenty mentions,
  immediate approved/rejected/all-status changes, source evidence, text/type filters,
  actual UI approval/rejection, canonical entity navigation and proposed-edge changes
  without pressing Apply. Screenshot inspected: `.local/candidate-review-fixed.png`.
- The supplied PDF passed actual import, background extraction and real spaCy processing:
  **1,130 proposals**, all checked against exact UTF-16 snapshot passages. Direct comparison
  with the unfiltered model found 1,235 occurrences, including 105 quantity results;
  retained results represent 396 distinct text/type pairs in 11,447 source words.
  These counts describe model suggestions, not independently verified identities.
- Existing real NLP/outage/retry/reprocessing/restart smoke passed and retained review
  decisions and original bytes. Existing 100-node/300-edge Edge smoke passed filters,
  citations, review, paths and persistence: 272 ms render, 1,106 ms initial load on this
  machine. Other unrelated smoke suites were not rerun for this correction.
- Temporary test services stopped afterward. The normal workspace was inspected read-only;
  the user's supplied PDF and existing screenshot changes remain untouched/uncommitted.

To use the correction, stop the current API/Web processes before rebuilding their Release
configuration, then restart API, Web and NLP. No migration or data deletion is needed.
The English model still produces false positives and omissions: review/correct its findings;
ordinary source text is preserved in Extracted text even when not recognized as a candidate.

## Post-Phase 7: bulk candidate approval

Completed on 2026-11-12 (America/New_York). Added an explicit analyst action to approve
matching proposed entity mentions across every page, preserving the defense-focused UI
styling and the API/Core/Infrastructure separation.

- Source review scopes the batch to that source; investigation Entities scopes it to the
  investigation. Existing candidate type/text filters apply. A read-only preview lists
  the eligible count before a separate confirmation; cancel changes nothing.
- Optional grouping is off by default. Explicit grouping creates one new entity per
  case/whitespace-normalized name and original type within the selected batch. Default
  behavior creates one new entity per mention. Neither option reuses or merges existing
  entities or fuzzy/alias matches. The preview shows the resulting entity count.
- Confirmation freezes the selected IDs. New proposals arriving afterward are excluded;
  a stale, reviewed, missing or foreign selection rejects the entire batch. A single
  transaction commits approval, canonical records, per-mention audits and a batch summary.
- Approved/rejected decisions remain intact. Original text, model labels, snapshot hashes
  and exact UTF-16 passages are retained. Reviewed timestamps materialize as UTC (a test
  caught the existing missing DateTime-kind converter; no DDL migration is needed).
- Limits: 5000 matching proposals per preview/request. Narrow filters above that bound.
  Blank or over-200-unit names are counted as skipped and remain proposed for individual
  canonical-name correction. Repeat submission of an approved batch returns 409.
- Added preview/approval API contracts, typed Web client methods and existing-style UI
  controls; no CSS, font, color, graph rendering or portfolio screenshot changes.
- Extended the existing candidate-review browser smoke, including optional local-PDF
  import/bulk approval. Updated README, setup and API documentation.

Verification:

- Debug build: passed with zero warnings/errors; running Release processes were not stopped.
- **179 .NET tests passed** (79 unit, 100 integration), including eleven new bulk cases.
  **64 Python tests passed**. Total: **243 passed**, zero failures/skips. The existing
  upstream Starlette/httpx test-client deprecation warning remains.
- Tests cover all-page approval, opt-in normalized grouping, type separation, no reuse
  of prior canonical entities, source/type/search filters, preserved decisions and exact
  provenance, UTC review timestamps, shared audit batch IDs, restart persistence, stale
  selection rollback, duplicate/empty/unconfirmed/foreign/missing/overlong selections,
  candidates arriving after preview and the 5000-record boundary.
- Real Edge/API/spaCy workflow passed preview/cancel, filter-change cancellation, default
  ungrouped counts, optional grouping and approval of 72 mentions across multiple pages
  into three new entities while preserving existing approved/rejected findings. Capture:
  `.local/bulk-approval-complete.png`.
- An isolated copy of the supplied DNI PDF passed actual import/extraction and approval
  of all **1130 mentions into 391 entities** with explicit grouping. The difference from
  396 raw text/type pairs reflects case/whitespace normalization. Normal investigation
  data and the supplied original file were not modified.
- EF model consistency: no pending changes. .NET format, Ruff lint/format and
  `git diff --check`: passed. No dependency or database migration was added.
- All temporary browser/API/NLP instances stopped afterward. Unrelated live smoke suites
  were not repeated for this addition. Source changes remain uncommitted for review.

Rebuild the configuration used by your running services and restart API/Web to use the
feature. Bulk approval retains the model's proposed classifications; it does not validate
their factual accuracy. Relationship generation stays an explicit subsequent action.

## Post-Phase 7: container and source-import alignment

Completed on 2026-11-12 (America/New_York), preserving the existing defense-focused
palette, typography, panel styling and navigation.

- Removed inconsistent first-child margins from the shared list/inspector layout.
  Claims, chronology and location review headings previously started 26 pixels below
  the neighboring empty/selected inspector. Both columns now share the same top edge;
  relationship review uses the same rule. Later section spacing and responsive stacking
  retain their existing behavior.
- Removed the redundant selected-filename/byte-count paragraph from source import.
  The browser's existing inline filename beside Choose File remains available.
- Anchored form-field contents to the top of their grid cells. This also eliminates
  the remaining 2.17-pixel control offset caused by a taller native file input stretching
  the adjacent input-method field.
- Updated design notes. This correction changes only app.css and SourceLibrary.razor;
  earlier bulk-approval work remains intact.

Verification:

- Debug Web build passed with zero warnings/errors; .NET formatting and
  `git diff --check` passed.
- Isolated real API/Web/NLP services loaded fictional Operation Meridian for an Edge
  browser audit. Seventeen desktop layout states passed with zero top-edge difference:
  populated/empty/selected claims, timeline, locations and relationships; investigation
  overview, source detail, entity detail, report history and workspace backup.
- Actual file selection retained native `d01.txt`, rendered no duplicate caption,
  preserved automatic title population and caused zero vertical movement. File and
  input-method controls had identical top coordinates after selection.
- Eighteen additional responsive page checks passed at 1100, 768 and 390 pixels across
  assessments, timeline, geography, relationships, source library and reports, with no
  horizontal page overflow. Desktop and mobile screenshots were inspected under ignored
  `.local/layout-after-*`; geometry is in `.local/layout-after-results.json`.
- The audit used temporary ports/data and stopped its own services afterward. Existing
  user services, investigation data and portfolio screenshots were left untouched.
  Backend/Python unit suites were not repeated for this CSS/markup-only correction;
  other browser engines and physical mobile devices remain unverified.

Rebuild/restart Web in the configuration you use, then refresh the browser to load
the corrected styles and source-import markup.

### Claims alignment follow-up

The running app confirmed the previous CSS was loaded, but the earlier check aligned
the Claims heading with the inspector border rather than aligning the two content
boxes. Moved the Claims heading above the review row, retaining the count over the
left column and full-width heading on stacked screens. No data or theme changes.
Debug Web build passed with zero warnings/errors. Browser verification uses the
content boxes as the alignment target; backend/Python suites are not repeated for
this markup/CSS correction. Release Web build also passed with zero warnings/errors;
formatting and diff checks passed. An Edge HTML/CSS fixture confirmed matching content
box tops (183px each). Full application retesting was blocked by Windows Event Log
errors in temporary services; the existing running app was inspected read-only before
the correction. Responsive application behavior and the selected inspector were not
reverified in this follow-up.
