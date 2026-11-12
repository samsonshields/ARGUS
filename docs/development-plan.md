# ARGUS development plan

Current scope: Phases 1 and 2. Phase 3 has not begun.

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

Next: await user authorization for Phase 3. No FastAPI, spaCy, NLP candidates,
processing-job subsystem, entity review, graphs, timelines or maps were implemented.
