# Phase 7: quality, performance and workspace recovery

## Backup export

`POST /api/v1/backups/export` returns an `application/zip` attachment with `no-store`
and `nosniff` headers. The Workspace backup page requires an explicit complete-workspace
selection and provides preparation/success/failure feedback. Its download proxy streams
API output; the browser downloads a bounded Blob only after a successful response.

The archive includes:

- `database/argus.db`: a consistent SQLite online-backup snapshot, including committed
  WAL content, all investigations, findings, audit history, queue history and saved reports.
- `documents/<generated-source-key>`: every original referenced by that snapshot.
- `manifest.json`: format version 1, GUID identity, UTC creation time, migration history,
  file paths, lengths and SHA-256 values.

Originals are checked against their immutable stored size/hash while copied. Database
integrity and foreign keys are checked. Only referenced originals are included; there
is no recursive directory archive, secret/configuration export or nested backup export.
Original keys are validated against their source GUID. Startup has no fixture seeding.

Preparation is limited to one export at a time, 5000 documents, 512 MiB total uncompressed
database/originals, 100 MiB per original and a two-minute I/O deadline. SQLite's native
snapshot operation is synchronous and is bounded by the database size rather than a
hard kill timeout. Larger workspaces fail with 422 rather than producing partial backups.
Another preparing export returns 409. Missing/modified originals block export. The
staging snapshot is converted to DELETE journal mode and its sidecars are cleaned up.
Successful streamed archives are deleted on close; normal failures/cancellation remove
partial output. A hard process crash during preparation can leave temporary files in
the data directory's exports folder; no source originals are automatically removed.

## Restore

`Argus.Api --restore-backup <archive.zip> <new-data-directory>` runs before host startup
and exits with 0 on success, 1 on validation/I/O failure, 2 on invalid command usage.
Success emits a JSON backup identity, document count and destination. There is no
HTTP restore endpoint that can replace a running database.

Restore refuses any existing destination, stages under the chosen parent, validates
the entire archive, and installs by directory rename only after validation. It rejects
unexpected/duplicate paths, traversal/absolute paths, symlinks, incompatible manifest or
migration histories, invalid sizes, excessive expansion, truncated entries, checksums,
database integrity/foreign-key failures and database/manifest document mismatches.
No arbitrary archive path is passed through to extraction. Failed staging directories
are removed. The original archive and any existing workspace are never overwritten.

Checksums detect corruption, not authenticity: archives are not signed/encrypted.
Restore only archives whose origin you trust. Backups include complete research data;
store them appropriately. Models, dependencies, binaries and environment variables
are installed/configured separately using setup.md. Start one API against the restored
directory; persisted interrupted jobs retain their existing bounded recovery behavior.

## Database query and graph budgets

Claim-list evidence counts use one grouped query for the whole page. Timeline/event
entity links and citations, and location names/citations, are retrieved in batches.
No query count grows by one or two per rendered row. Evidence retains exact UTF-16
quotation semantics and review/revision behavior after batching.

WorkspaceQuality adds indexes for claim/active/evidence-kind, event/active,
location/active and entity/mention-status lookups. Existing records are unchanged.
Tests count real EF commands for fifty records, enforcing at most five claim-list,
five timeline and four location-list read queries. Dashboard HTTP reads run concurrently
through separate clients; a scoped EF context is never used concurrently.

Existing graph correctness/performance gates cover 100 nodes/300 edges with a three-second
Core algorithm budget and a five-second browser-render budget. Real timings are recorded
in development-plan.md and depend on the machine. Labels wrap, layouts include label
dimensions, and HTML selectors remain available for crowded views. Large graph/export
caps from previous phases remain; no silent truncation was introduced.

## Diagnostics and UI quality

.NET application logs use JSON UTC timestamps/scopes. API completion events include
HTTP method, route template, status, duration and trace ID; `X-Argus-TraceId` is returned.
EF SQL and outbound HTTP informational logs are suppressed to avoid noisy SQL and query
text. Domain mutations remain in persisted audit history. Python application errors
use JSON timestamps/category/pipeline/source ID and omit submitted text/parser details.
Infrastructure server access logs may retain ordinary framework formatting.

The shared error view includes a concrete reload action and avoids showing a loading
indicator after a failed request. Network failures receive a plain API-reachability
message. Empty modules point toward collection/review. Context navigation, grouped
filters, compact related fields, keyboard focus, skip navigation, responsive wrapping
and reduced motion preserve the dark workstation layout. Design decisions are in
design.md; portfolio images contain only the fictional Meridian investigation.

Regression checks use isolated temporary databases/data roots. The clean-source check
uses Git's tracked/unignored file list and an allowlist of source directories, so ignored
schema files cannot be hidden by local build output. `.gitignore` distinguishes runtime
data from Infrastructure's Data source directory, which Windows otherwise matched.
