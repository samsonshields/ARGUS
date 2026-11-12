# ARGUS

Local-first OSINT analysis workstation for Windows. Phases 1-5 provide investigations,
preserved source documents, asynchronous local entity recognition and analyst review.
Approve/correct/reject mentions, inspect exact source evidence, associate mentions
with existing entities and explicitly merge duplicate records. Explore evidence-linked
relationship graphs and review sentence co-mentions. Build reviewed timelines and
geographic views with precise source citations. **Phase 6 has not begun.**

## Requirements

- .NET 10 SDK (verified SDK 10.0.102, runtime 10.0.2). SDK pinned in global.json.
- Python 3.12+ with a virtual environment (verified 3.14.6, Windows x64).
- Internet access for initial NuGet/Python/model installation. Runtime processing is offline.
- No Docker, Node, external LLMs or paid APIs required.

## Local startup

From the repository root in PowerShell:

```powershell
python -m venv intelligence-service/.venv
intelligence-service/.venv/Scripts/python.exe -m pip install -r intelligence-service/requirements-dev.txt
intelligence-service/.venv/Scripts/python.exe -m spacy download en_core_web_sm
intelligence-service/.venv/Scripts/python.exe -m pip check
```

Tested: spaCy 3.8.18, FastAPI 0.162.4, Pydantic 2.14.3, Uvicorn 0.54.0 and
`en_core_web_sm` 3.8.0. `intelligence-service/requirements-lock.txt` captures the full
tested environment, including the pinned model URL/hash; install that instead of
requirements-dev/model download to reproduce the Windows/Python 3.14 environment.
No model download is attempted by the running service. A missing/incompatible model
returns a 503 readiness error; restart the service after installing the model.

Start the local NLP service in one terminal:

```powershell
intelligence-service/.venv/Scripts/python.exe -m uvicorn app.main:app --app-dir intelligence-service --host 127.0.0.1 --port 8000
```

Start the API in another terminal:

```powershell
dotnet tool restore
dotnet restore Argus.sln
dotnet build Argus.sln --no-restore
dotnet run --project src/Argus.Api --no-build --launch-profile http
```

Start Web in a third terminal:

```powershell
dotnet run --project src/Argus.Web --no-build --launch-profile http
```

Open **http://localhost:5200**. API: **http://localhost:5100**; health: `/health`.
NLP readiness: **http://localhost:8000/health**; interactive NLP contract docs: `/docs`.
.NET OpenAPI: **http://localhost:5100/openapi/v1.json**.
Stop each application with Ctrl+C. Stop existing instances before rebuilding. API startup applies committed migrations before
serving requests. Default database: `src/Argus.Api/data/argus.db` using these commands.
Keep the complete data directory (database and `documents/`) between restarts. No internet connection is required for normal local use.

Set `$env:ARGUS_DATA_DIRECTORY = 'C:/Argus/data'` in the API terminal to override
storage. Set `$env:Api__BaseUrl = 'http://localhost:5100'` in the Web terminal to
override its API address. `.env.example` is reference only; .NET does not auto-load it.
Keep servers bound to loopback: this single-user application has no authentication
and is not ready for public hosting.

Optional explicit migration application (startup already handles this):

```powershell
dotnet ef database update --project src/Argus.Infrastructure --startup-project src/Argus.Api
```

## Validation

```powershell
dotnet test Argus.sln --no-restore
dotnet format Argus.sln --no-restore --verify-no-changes
python scripts/smoke.py
python scripts/smoke_nlp.py
intelligence-service/.venv/Scripts/python.exe scripts/smoke_graph.py
intelligence-service/.venv/Scripts/python.exe scripts/smoke_temporal.py
intelligence-service/.venv/Scripts/python.exe -m pytest intelligence-service/tests -q -c intelligence-service/pyproject.toml
intelligence-service/.venv/Scripts/python.exe -m ruff check intelligence-service/app intelligence-service/tests
intelligence-service/.venv/Scripts/python.exe -m ruff format --check intelligence-service/app intelligence-service/tests
```

The smoke script defaults to a Debug build. It launches temporary loopback API/Web
processes, checks rendered pages/CSS, imports text/PDF/pasted sources, verifies hashes,
duplicates, failures, search, downloads, extraction retry and restart persistence.
It cleans up its processes and temporary database; diagnostics
are in ignored `.local/smoke.log`. It does not drive browser clicks or Blazor circuits.
The compatibility smoke explicitly disables automatic NLP and the worker to test
Phase 1/2 APIs in isolation. `smoke_nlp.py` launches all three real processes with
automatic processing enabled, tests entity review, service outage/retry and queued
work across restarts. It requires the installed venv/model and writes `.local/smoke-nlp.log`.
The graph smoke uses the real NLP/API workflow to create 100 explicitly reviewed
fictional identities and 300 edges, then drives headless Microsoft Edge using Playwright.
It tests graph controls, citations/review, offline assets and restart persistence.
It requires Edge (or set `ARGUS_BROWSER_CHANNEL=chrome` for installed Chrome), the
development Python requirements and a build. No browser download is required with Edge.
Artifacts are ignored under `.local/smoke-graph.log` and `.local/phase4-graph*.png`.

Manual UI check: create an investigation, edit its scope/status, filter by Archived,
cancel a deletion, restart both apps, reopen the record, and confirm deletion.
Check blank titles and reversed dates produce validation messages.

For verification while Debug instances are running, build/test with `-c Release`,
then set `$env:ARGUS_BUILD_CONFIGURATION = 'Release'` before running the smoke script.
Those checks use separate processes and temporary data. Restart your normal API/Web
instances after building to load the new code and apply the migration.

## Import documents

Open an investigation, select **Source library**, then choose Upload file or Paste text.
Enter the title, optional publisher/URL/publication date and manual source reliability.
URLs are attribution only and are never fetched. Open a source to read escaped extracted
text, download the original, edit metadata or retry extraction. Reliability is an analyst
assessment of the source, not confirmation of any claim.

Try the clearly fictional `.txt` and `.pdf` fixtures in `sample-data/documents/`.
Uploading the same bytes again within one investigation returns a duplicate error with
a link to the existing source. The same document may be used in different investigations.
Investigations containing sources are protected from deletion; archive them instead.

New imports return promptly after preserving the original and queuing a persistent
job. Text extraction and NLP run in the background. Malformed PDFs, scanned/image-only
PDFs, unsupported encodings and extraction-limit failures retain their originals
and show a Failed source/job with a safe error message.
Retry replaces extracted text only on success; previous successful text survives failures.
There is no OCR. PDF layout/text order can vary; use the original when checking citations.

Default limits (API environment-variable overrides):

| Setting | Default | Environment variable |
| --- | --- | --- |
| File size | 10 MiB | `Documents__MaxFileBytes` |
| Extracted text | 2,000,000 UTF-16 characters | `Documents__MaxExtractedCharacters` |
| PDF pages | 500 | `Documents__MaxPdfPages` |
| Extraction deadline including queue | 20 seconds | `Documents__ExtractionTimeoutSeconds` |

Supported text: strict UTF-8, or BOM-marked UTF-16/32. Originals are byte-preserved;
extracted text uses LF line endings and trims outer whitespace/BOM. PDF extraction uses
PdfPig 0.1.16 in a child mode of the installed API executable; it requires `dotnet` on PATH,
has a 256 MiB managed heap cap, and is terminated on timeout. Files are parsed as data,
never executed. One extraction and one background job run at a time.
Keep a stopped-application backup of the complete data directory. A hard crash between
file creation and database commit can leave an unreferenced file; there is no automatic
orphan cleanup. Do not delete preserved documents manually.

Manual Phase 2 UI check: upload both fixtures, reopen after restarting, search for
`logistics`, filter by publication date, try a duplicate and an unsupported file,
paste a source, update reliability, retry extraction, and download the original.

## Process and review entities

Imports automatically queue processing. Existing Phase 2 sources are not processed
retroactively; open a source and select **Queue entity processing**. The source page
and **Processing jobs** monitor refresh job state every two seconds. You can cancel
active jobs or retry failed/cancelled jobs. Text-only retry remains available when
there is no active job, for troubleshooting extraction without NLP.

Once processing completes, use **Entity review** on the source or investigation page.
Select a proposed mention to see its exact highlighted passage, original model label,
model version and evidence offsets. Correct the canonical name/type, then approve or
reject it. Confidence remains unavailable: spaCy does not provide a calibrated probability.
Approving creates a canonical entity or associates the mention with an explicitly
selected suggested entity. Name similarity is not proof of identity.

The approved entity page supports corrections, aliases retained from renames and
confirmed merges. Merging moves approved mentions but keeps their original text,
model labels and snapshot references. Records from different investigations cannot merge.
Entity merges preserve relationship citations too. Self-links caused by a merge leave
the active graph but remain accessible in relationship history.

Jobs persist as PENDING/RUNNING/COMPLETED/FAILED/CANCELLED. Interrupted RUNNING jobs
recover on API startup, retaining their attempt budget. Transient errors retry at most
three attempts per job, five seconds apart by default. Start/fix Python and manually
retry a failed job after an outage. Cancellation discards late results; it does not
forcibly interrupt an already running Python inference. Reprocessing identical text
reuses stable mention records and preserves approvals/rejections; changed text gets
a new immutable snapshot, while older reviewed evidence remains accessible.

NLP defaults: `Intelligence__BaseUrl=http://localhost:8000`, `Intelligence__TimeoutSeconds=30`,
`Intelligence__MaxAttempts=3`, `Intelligence__RetryDelaySeconds=5`,
`Intelligence__MaxTextCharacters=250000`, `Intelligence__MatchThreshold=0.85`.
Python uses `ARGUS_SPACY_MODEL=en_core_web_sm` and `ARGUS_NLP_MAX_UTF16=250000`.
Keep the character limits aligned. Longer documents fail clearly, without truncation.
Offsets count UTF-16 code units, with inclusive starts and exclusive ends, indexing
the exact preserved extracted text. See [NLP contract](docs/nlp-contracts.md).

`Intelligence__WorkerEnabled=false` pauses automatic worker execution without deleting
queued jobs. `Intelligence__AutoProcessImports=false` retains the Phase 2 synchronous
text-only import behavior; explicit `/process` still queues NLP. These switches support
isolated tests and diagnostics, not a production fake-NLP mode.

Manual Phase 3 check: import a fictional report naming people/organizations/places;
wait for completion; inspect a highlighted mention; approve one and reject another;
associate a second mention explicitly; correct/merge duplicate entities; stop Python
and observe bounded failures; restart/retry; restart API and verify reviews/evidence persist.

## Relationship review and graph

After approving entity mentions, open **Relationship review** in the investigation and
select **Generate co-mention candidates**. Each eligible source queues a RELATIONSHIPS
job. Refresh the review list after it completes; automatic generation never approves edges.
Python uses deterministic sentence boundaries over exact snapshots, with at most one
candidate per distinct entity pair per sentence. Different sentences are not connected.

Select a relationship to inspect its exact quoted passage and document link. Check
the passages you reviewed and explicitly confirm review. Keeping MENTIONED_WITH
records only that the entities occurred in the sentence. A substantive type requires
analyst reasoning; directed types also offer a reverse-direction control. Rejected
proposals and their citations remain in history. New passages attached to an already
reviewed co-mention remain proposed until individually reviewed.

Open **Relationship graph** for pan/zoom, filters, node lookup/selection, unique-neighbor
and incident-edge counts, component membership, degree centrality and betweenness.
Select an edge for its type, status and cited passages. The HTML selectors provide
keyboard access alongside the canvas. **Focus selected node** narrows the current view;
**Expand neighbors** reveals its cached neighbors; **Show full graph** restores the view.
Shortest paths can optionally respect directed edges. Paths use the applied filters.
Graph interactions do not modify underlying evidence.

Visual key: dashed amber = proposed; dotted gray = reviewed co-mention; solid cyan =
analyst-reviewed substantive relationship. Co-occurrence is never proof of a substantive
relationship. Metrics describe a simple undirected graph projection, not the intent,
importance or threat level of a person. Parallel reviewed edges remain distinct, while
centralities count unique neighbors. Rejected/retired edges are excluded from the graph.

Cytoscape.js 3.33.4 is vendored locally with its MIT license and verified package
integrity; runtime graph rendering uses no CDN. Layouts use COSE for up to 300 nodes
and a grid beyond that. Graph responses are bounded to 2,000 nodes / 10,000 edges;
apply filters when larger. Relationship/evidence collections are paginated.
Co-mention jobs are bounded to 50 snapshots and 5,000 candidate passages per job,
5,000 input mentions per snapshot, 8,000 UTF-16 units per citation and a 120-second
total generation deadline. Existing NLP HTTP/input/response/retry limits still apply.

Manual Phase 4 check: approve two entities in one source sentence, generate proposals,
inspect/review a citation, compare proposed versus reviewed edge styles, filter, select
a node, expand neighbors, highlight a path, restart and reopen the evidence.

## Timeline and geography

Open an investigation and choose **Timeline and event review**. Select a source under
**Extract date candidates**, queue extraction, monitor **Processing jobs**, then select
Proposed and refresh. A completed entity/text job must precede event extraction.
Alternatively choose **Create event**, enter a title, type and explicit date precision.
Date mentions remain proposals until you inspect the source and approve them.

EXACT uses yyyy-MM-dd (with an optional end date), MONTH uses yyyy-MM, YEAR uses yyyy,
and UNKNOWN keeps both fields empty. Relative phrases and ambiguous slash dates stay
unknown; no reference date, locale or missing year is invented. Month/year events retain
their precision on the timeline. Filters use possible date overlap and can explicitly
include unknown dates. Select a row to read preserved citations, associate entities or
an approved location, correct the date/title, reject or confirm removal. Add a source
passage before approving manual events. Corrections and citation changes return records
to Proposed; approval then updates the visible timeline without restarting.

Open **Geography and locations** to create a profile for an approved LOCATION entity.
Find/select the entity, choose UNKNOWN, APPROXIMATE or EXACT precision, and enter
explicitly selected or source-verified coordinates. Approximate locations need an
uncertainty radius in metres; unknown locations must have no coordinates. Explain the
basis, confirm coordinates, save, add a source citation, then explicitly approve.
Clicking the map copies coordinates into a draft only. No automatic geocoding runs.

The map plots approved known locations and approved events linked to those locations.
Amber circles show uncertainty; purple rings identify events. Select a marker or use the
keyboard selector to open citations and navigate to sources, entities or the timeline.
Event date/type filters affect event rings; location entities stay visible. Pan/zoom and
**Save current geographic focus** persist a preferred viewport. Retiring/correcting a
profile hides its markers until reviewed again while preserving event/source history.
Merging two entities with active profiles requires explicitly retiring one profile first.

Leaflet 1.9.4 is vendored locally. The default coordinate grid and all reviewed markers
work offline. **Load OpenStreetMap basemap (online)** explicitly enables external viewport
tile requests with attribution and browser caching; tiles are not bulk-downloaded.
An unavailable tile service leaves the grid and markers usable. This optional mode
reveals the viewed area to the tile provider. See [temporal/geographic contracts](docs/temporal-contracts.md)
for rules, limits, APIs and the linked provider policy.

`smoke_temporal.py` launches three isolated real processes and headless Edge, drives
review/date edits/map selection/filtering/draft coordinates, verifies exact citations,
unknown dates, optional tile failure, deduplication and restart persistence. It blocks
external requests; successful online tile imagery is therefore not verified. Screenshots
and logs are ignored under `.local/phase5-*.png` and `.local/smoke-temporal.log`.
Verified Phase 5 gate: 130 .NET tests (67 unit/63 integration) and 53 Python tests passed;
Release build had zero warnings/errors. Formatting, lint, migration consistency, NuGet
vulnerability audit, dependency checks and all four live smoke workflows passed. Browser
coverage targets Edge desktop; the Python test client emits one upstream deprecation warning.
See the [development plan](docs/development-plan.md) for the complete verification record.
Restart API, Web and Python after rebuilding to load Phase 5 and apply its migration.

## Structure

- `Argus.sln`: Web, API, Core, Infrastructure, unit and integration test projects.
- `src/Argus.Core`: domain rules, DTOs and repository interface.
- `src/Argus.Infrastructure`: EF Core SQLite persistence, migrations, local document storage and text/PDF extraction.
- `src/Argus.Api`: versioned controllers and application services; owns all writes.
- `src/Argus.Web`: Blazor UI and centralized HTTP client; no database dependency.
- `tests/`: domain unit tests and isolated temporary SQLite/API integration tests.
- `scripts/`: live HTTP smoke verification.
- `intelligence-service/`: independent FastAPI/spaCy service and Python tests; no database access.

EF Core and EF tool are pinned to 10.0.2. SQLitePCLRaw bundle 3.0.5 replaces the
vulnerable native dependency pulled by EF's default bundle. Native Blazor forms and
custom CSS keep the shell small; MudBlazor compatibility was checked but it is not installed.

Microsoft.OpenApi is pinned to patched 2.7.5 rather than the vulnerable transitive 2.0.0.
The graph workflow has automated Edge browser coverage and screenshots. Broader
file-selection/entity-review UI workflows retain their manual checklists. English NER is
model-dependent and may miss or misclassify entities. Python test tooling currently
emits one upstream httpx deprecation warning; tests still pass. Assessments and reports
remain deferred to Phase 6.

See [specification](docs/project_specifications.md), [architecture](docs/architecture.md),
[API contracts](docs/api-contracts.md), [schema](docs/database-schema.md) and
[development plan](docs/development-plan.md).
