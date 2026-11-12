# Architecture through Phase 2

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

Python 3.14.6 meets the specified minimum. FastAPI/spaCy dependencies and compatibility
checks belong to Phase 3; the smoke script needs only the standard library.
No authentication, public deployment hardening, pagination or concurrent edit conflict
handling is included. Last saved edit wins. Loopback defaults support local use.

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

Synchronous request-scoped text extraction is deliberate. Durable processing jobs,
FastAPI, named entities and NLP integration remain unimplemented until Phase 3.
