# Architecture through Phase 6

The repository root is the proposed argus monorepo root. The supplied specification
is project_specifications.md; the requested hyphenated filename is absent. There was
no existing application or Git repository. Git initialization/commits were not performed.

Dependency direction: Web -> Core; API -> Core/Infrastructure; Infrastructure -> Core.
Web is a .NET 10 Blazor Web App with server interactivity. Browser actions travel over
the Blazor circuit; Web calls the separate API over HTTP/JSON using a centralized typed
HttpClient with a strongly typed base address and 15-second timeout. Web never accesses SQLite.
Controllers handle HTTP validation, application services coordinate domain/repository
operations, and domain entities revalidate before mutation. Only API owns database writes.

SQLite is file-backed, with migrations applied before API starts serving. No EnsureCreated
fallback is used. Run one API process per local database. GUID identities and UTC
system timestamps are distinct from nullable DateOnly research scope dates.

Validation/status errors use Problem Details; unexpected API exceptions use centralized
exception handling without returned stack traces. Structured application logs contain IDs,
not descriptions. Razor encodes user content. UI handles loading, empty/error states,
save feedback and separate deletion confirmation. Deletion also requires an API confirm flag.

Dashboard counts reflect investigations and Phase 2 source/extraction totals. Recent
investigation updates are not a full audit history. Later-phase metrics/navigation are omitted. Blazor
built-in forms/custom CSS avoid an unnecessary shell dependency; MudBlazor remains optional.

Python 3.14.6 meets the specified minimum. Phase 3 uses verified FastAPI/spaCy wheels
and a trained local English model; the smoke scripts themselves use the standard library.
Authentication/public deployment hardening is absent. Legacy investigation/source
lists remain unpaginated and their edits use last-write-wins; intelligence review and
jobs use pagination and optimistic concurrency. Loopback defaults support local use.

## Phase 2: source management

Core contains source metadata validation, immutable original references and explicit
success/failure extraction transitions. API SourceService coordinates EF persistence,
storage and extraction; no business logic or filesystem access lives in Blazor.
It uses the scoped EF context directly for source-specific projections and atomic audit
writes rather than adding another repository layer with no alternate implementation.
Web uses a centralized SourceClient and shared metadata fields. Original downloads
are proxied as attachments; imported text is rendered with Razor escaping.
The Blazor inbound message cap is 16 MiB to support bounded large pasted-text form
values; API JSON body limits account for Unicode escape expansion while the import
service separately enforces original UTF-8 byte limits.

Infrastructure supplies managed local storage, strict Unicode text decoding, LF
normalization and PdfPig 0.1.16 extraction. PDF parsing runs in a child mode of the API
executable, before host construction: no database, HTTP server or Python service runs
in that mode. Only installed .NET code executes; uploaded documents remain input data.
A singleton gate allows one extraction at a time. The configurable deadline includes
the queue; cancellation/timeout kills the PDF process and cleans its temporary output.
Page/text limits and a 256 MiB managed heap cap bound common parser resource use.
This is process isolation for local parsing, not a security sandbox or background-job system.

Successful imports preserve exact bytes and compute SHA-256 before extraction.
Deduplication is scoped to investigation and enforced by a unique database index.
Malformed/image-only/over-limit documents are preserved with Failed status. Failed
retries never erase prior successful text. Metadata, import and retry audit rows commit
with source changes. Parent deletion uses explicit RESTRICT constraints. Originals,
hashes and retrieval timestamps are immutable through the API.

Application data paths are resolved from final IConfiguration through DI, so host
overrides apply consistently to SQLite and document storage. Integration tests assert
the temporary database path. The first Phase 2 run exposed an earlier eager-resolution
bug: synthetic rows/files landed in the default database. Those identified artifacts
were backed up and removed; the fix and isolation regression check are in place.

Synchronous request-scoped text extraction was the Phase 2 behavior. Phase 3 moves
default import processing into the worker while retaining explicit text-only compatibility.

## Phase 3: persistent local NLP and analyst review

Default uploads preserve originals and atomically insert source/audit/job records,
then return a Pending source. IntelligenceWorker polls persisted pending work and
ProcessingRunner claims one job, extracts text, snapshots its exact contents, sends a
versioned JSON contract to FastAPI and validates the returned spans before saving
PROPOSED mentions. HTTP timeout, response-size, document and candidate limits are bounded.
The Python service is stateless with no database/storage dependencies or remote inference.
It loads only an installed English spaCy NER pipeline; unavailable models return 503.

Worker execution uses scoped services and a single local worker; no database transaction
is held across extraction/HTTP. A filtered active-job unique index prevents duplicate
queueing. Transient failures schedule bounded retries; explicit manual retries create
new history entries. RUNNING jobs recover on startup while keeping their attempt budget.
Job cancellation/review use optimistic revision tokens so late/stale results cannot win.
Cancellation does not kill an inference already running in the independent Python service.

Immutable SourceTextSnapshots identify text by source/hash. Mentions use exact UTF-16
spans, including surrogate-pair boundary checks. Reprocessing identical text reuses
snapshot/span/type records and preserves decisions; extraction changes never invalidate
old approved evidence. Confidence is null, not a fabricated model probability.

EntityReviewService creates canonical records only when an analyst approves a proposed
mention. Exact normalized names, aliases and deterministic Levenshtein-based similarity
produce suggestions; no person is automatically merged or associated by name. Existing
records require explicit selection. Confirmed merges move approved mention links,
retain the former record, preserve aliases and audit before/after state. Original text,
model labels and snapshot references are immutable. Revision tokens protect association,
correction and merge races; composite FKs prevent cross-investigation entity links.

Blazor centralizes these APIs in IntelligenceApiClient. Source/investigation review
panels show proposed/approved/rejected mentions and escaped highlighted evidence;
entity pages support corrections, paginated target selection and confirmed merging.
Job panels poll every two seconds and support cancellation/retry/readiness checks.
New collection APIs are paginated; legacy investigation/source arrays remain compatible.
.NET publishes OpenAPI JSON; Python publishes OpenAPI and its documentation UI.

The tested stack is Python 3.14.6, spaCy 3.8.18, en_core_web_sm 3.8.0, FastAPI 0.162.4,
Pydantic 2.14.3 and Uvicorn 0.54.0. Pinning Microsoft.OpenApi to patched 2.7.5 avoids
the vulnerable 2.0.0 transitive dependency of ASP.NET OpenAPI 10.0.2. Dependency/model
installation needs internet; runtime processing uses existing local assets offline.

Phase 3 did not add graph features. Phase 4 implementation is described below.
See nlp-contracts.md for entity wire details.

## Phase 4: relationship review and graph exploration

Relationship generation is a second persistent job kind. The .NET worker sends approved
mention/canonical IDs and immutable snapshot text to the independent Python sentence
pipeline. It validates returned IDs, hashes, offsets and quoted passages, stages all
results and commits proposals/evidence/job completion atomically. Stable evidence keys
preserve reviews under reruns; bounded retries and cancellation reuse existing job rules.
This pipeline deliberately cannot assert membership, location or participation.

Analysts select evidence passages and explicitly approve/reject proposals. Co-mentions
remain low-assertion even after review. Substantive classifications need a stated reason;
their direction is explicit. Newly attached passages remain proposed independently of
an already reviewed edge. Entity and relationship revision tokens prevent stale merges/
generation/review from overwriting concurrent decisions. Entity merges retain citations,
retire self-links, and preserve parallel edge history rather than silently consolidating it.

Read-only GraphService projects active canonical entities and proposed/reviewed edges
with citations. GraphAlgorithms in Core implements normalized degree, connected
components, unweighted shortest paths and Brandes betweenness. Metrics use a simple
undirected projection; paths optionally honor directed types. This small deterministic
.NET implementation avoids a runtime Python round trip for graph exploration and needs
no additional NetworkX dependency. It satisfies the implement-or-integrate algorithm
requirement while keeping Python responsible for stateless candidate processing.

GraphApiClient centralizes HTTP in Web. Cytoscape.js 3.33.4 is vendored with its license,
verified npm tarball integrity and JavaScript hash. Blazor interop serializes enum names
explicitly and manages per-component graph disposal. Graph layout/selection/filtering
are view operations only. DOM selectors provide keyboard alternatives to canvas nodes/
edges; highlighted paths and selected-edge citations remain linked to investigation context.
No CDN or remote assets are needed. The graph response/algorithm size caps are explicit;
larger graphs require filters rather than silent truncation.

API/domain/real-Python tests cover proposed evidence, review, retries, merge retirement,
exact snapshot provenance and graph invariants. The real-process Edge smoke builds its
100-node/300-edge fixture through ordinary API imports/approvals, not direct Python DB
writes, and verifies browser interaction/performance while blocking non-loopback assets.
Phase 5 was subsequently authorized and is described below.

## Phase 5: timeline and geography

Core defines AnalysisEvent, EventEvidence, EventEntity, LocationProfile, LocationEvidence
and GeographicFocus, with validated DTOs, interval/date rules, explicit precision and
optimistic revisions. TemporalSchema and the additive TimelineGeography migration keep
foreign keys restricted and associations within their investigation. TemporalService
coordinates review, snapshot citation selection, date generation, bounded map/timeline
queries and atomic audit writes. Controllers remain transport adapters. There is no
new database dependency in Web or any persistence dependency in Python.

EVENTS jobs reuse the persistent worker and bounded typed HTTP transport. Python detects
English/ISO date mentions and sentences without resolving relative dates or inferring a
locale/year. The API independently validates hashes, UTF-16 spans, quoted text and date
values. Stable snapshot/span/method keys preserve decisions and removals across reruns.
Source extraction status is unaffected. Results/job/audits commit atomically after HTTP;
cancellation/restart behavior is shared with the existing queue. Snapshots can be staged
in the result transaction for events, rather than committed before their HTTP call.

TemporalApiClient is the sole HTTP entry point for the new Blazor pages. Timeline and
map use explicit saves/reviews, shared snapshot citation controls, safe Razor text and
DOM textContent labels. Unsaved form drafts cannot be approved; save/reload first.
Leaflet 1.9.4 renders offline coordinate grids, approved markers and uncertainty circles.
The optional online OSM basemap is opt-in, uses normal browser caching/identification,
keeps attribution visible and has no proxy or bulk download. Map clicks affect drafts;
pan/zoom affects view state. Only explicit focus saving writes the viewport preference.
Source/entity/timeline links retain investigation context. Details and keyboard selectors
provide access alongside SVG markers. Enum names are explicitly serialized for JS.

Entity merges redirect event links and location entity references; two active profiles
require explicit retirement before merging. Corrected or citation-edited records need
fresh approval. Soft removal retains citations, provenance and audit history. Unknown
locations are retained for review but never represented as precise points. See
[temporal contracts](temporal-contracts.md) for limits and wire semantics.

Phase 6 was subsequently authorized and is described below.

## Phase 6: analyst assessments and report snapshots

Core adds Claim, ClaimEvidence, GeneratedReport, confidence/evidence enums and validated
DTOs. ClaimService coordinates analyst-authored statements, manual confidence, exact
source passages, support/contradiction/context labels and explicit review. Optimistic
claim revisions also protect evidence updates; changes require renewed review and clear
previous conflict resolutions. Before/after audit records preserve corrections, source
provenance and manual confidence changes. No automated confidence scoring or model-generated
claims were introduced. Python processing remains unchanged and is not needed to author
claims or export reports from existing sources.

ReportService selects approved active material, validates references and original-file
hashes, and persists the completed Markdown artifact/audit in one SQLite transaction.
Report generation is bounded and deterministic; MarkdownReport in Core formats the ten
sections and escapes untrusted text. Missing optional modules remain explicit, unknown
precision is preserved, and conflicts remain visible. Report artifacts contain Markdown,
UTC generation time, SHA-256 and validation metadata; there is no edit/delete endpoint.
Downloads recheck their content hash. Snapshot approvals are historical, not live review
state. Sources and snapshots are retained independently of later assessment edits.

AssessmentsReports is an additive migration with restricted finding/evidence/report
foreign keys. Web uses AssessmentApiClient and no database access. Focused Blazor pages
provide claim/evidence review, source grades, conflict filters, final validation, an
encoded preview and attachment downloads through Web's API proxy. A CommonMark parser
is used only for smoke verification, not in the production request path. The verification
also found and corrected SQLite-vs-UTF-16 substring mismatch: citation slicing now stays
in .NET, including temporal quotations used in reports.

Phase 7 quality/performance/backup/demo/portfolio work has not been started. See
[assessment contracts](assessment-contracts.md) for explicit export bounds and semantics.
