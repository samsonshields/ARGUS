# Database schema (Phases 1–4)

File: API data directory / argus.db. Committed migrations and snapshot are under
src/Argus.Infrastructure/Data/Migrations. Initial migration creates Investigations:

| Column | Storage and rule |
| --- | --- |
| Id | TEXT GUID primary key |
| Title | TEXT NOT NULL; trimmed length 1-200 CHECK |
| Description | TEXT NOT NULL; API/domain max 5000 |
| Status | TEXT NOT NULL; Active/Archived CHECK; indexed |
| CreatedAt | TEXT UTC timestamp; immutable via API |
| UpdatedAt | TEXT UTC timestamp; indexed |
| StartDate | nullable TEXT ISO date |
| EndDate | nullable TEXT ISO date; end >= start CHECK when both known |
| Region | nullable TEXT; API/domain max 200 |

UTC kind is restored during EF materialization. SQLite MaxLength does not constrain
TEXT; API/domain validation enforce description/region limits. EF owns migration
history and migration lock tables. Foreign keys are enabled on connections; no dependent
entities exist in Phase 1. Deletion removes only an investigation. Future schemas
must specify dependent deletion restrictions/provenance before introduction.

Do not commit database/WAL files. Stop the API before copying its full data directory
for manual backup. Never use EnsureCreated on this migration-managed database.

## Phase 2 migration: SourceDocuments

The additive migration creates Sources and AuditEvents without rewriting investigation
records. Upgrade from the initial Phase 1 migration is integration-tested.

Sources: GUID Id/InvestigationId; Title; SourceType (Text/Pdf/PastedText); optional
OriginalUrl/Publisher/PublishedAt (DateOnly); UTC RetrievedAt; manual Reliability A-E;
FilePath (generated relative storage key); sanitized OriginalFileName; FileSize;
lowercase SHA-256 ContentHash; ExtractedText; ProcessingStatus (Ready/Failed);
nullable ProcessingError/ExtractedAt; ExtractionVersion. There is no NLP state.

Unique `(InvestigationId, ContentHash)` guards exact duplicates, including concurrent
requests. Additional indexes cover ContentHash, PublishedAt and ProcessingStatus.
CHECK constraints enforce title length, positive file size, hash length and valid
type/status/reliability values. API/domain validation enforce other string lengths.
Physical paths are not returned by the API.

AuditEvents: GUID Id/InvestigationId/RecordId; RecordType; Action; BeforeJson/AfterJson;
UTC OccurredAt; Actor ('Local application user', not an authenticated identity).
Metadata snapshots exclude full source text. Index: `(InvestigationId, OccurredAt)`.

Both tables have explicit RESTRICT foreign keys to Investigations. Investigation
deletion is now rejected with 409 when sources exist; archive instead. Phase 2 does
not expose deletion of sources or audit history, preserving original evidence.

Originals live under `data/documents/{source-guid-without-dashes}.txt|pdf`, outside
Web static assets. Filenames supplied by users never determine physical paths.
Database/audit changes commit atomically. Failed commits remove the new unreferenced
file; extraction failures preserve originals in a Failed source. Filesystem and SQLite
cannot share a transaction: a hard process/machine crash can leave an orphan file.
Automated orphan cleanup is deliberately absent to avoid deleting evidence.

## Phase 3 migration: EntityProcessing

The migration expands the source status constraint to Ready/Failed/Pending/Processing
and adds four tables. Phase 2 upgrades preserve existing source metadata, text,
hashes and original references; this upgrade is integration-tested with real stored bytes.
Existing sources are not automatically queued during migration.

| Table | Important fields and invariants |
| --- | --- |
| ProcessingJobs | GUID Id/SourceId/InvestigationId; State; Attempts; ErrorCode/Error; UTC CreatedAt/UpdatedAt/NextAttemptAt; nullable SnapshotId; CandidateCount; Revision concurrency token |
| SourceTextSnapshots | GUID Id/SourceId; immutable Text; SHA-256 TextHash; UTC CreatedAt; unique SourceId+TextHash |
| Entities | GUID Id/InvestigationId; Name; NormalizedName; Type; AliasesJson; nullable MergedIntoId; Revision concurrency token |
| EntityMentions | GUID Id/SourceId/InvestigationId/SnapshotId/ProcessingJobId; immutable Text/OriginalType/Start/End/ModelName/ModelVersion; Status; optional EntityId/ReviewedAt; Revision concurrency token |

ProcessingJobs has a filtered unique SourceId index for PENDING/RUNNING and a
State+NextAttemptAt index for worker polling. Jobs reference sources, investigations
and snapshots with RESTRICT foreign keys. Attempts increment when claimed and remain
bounded through recovery; manual retry creates a new persisted job/history entry.

Mentions uniquely identify SnapshotId+Start+End+OriginalType. CHECK constraints enforce
nonempty positive spans and require EntityId only for APPROVED mentions. Composite
foreign keys ensure the snapshot belongs to the same source and an approved entity
belongs to the same investigation. Mentions retain first-generation model/job provenance;
reprocessing never silently resets approval/rejection or deletes reviewed evidence.

Entities are created only through analyst approval. Names are deliberately not unique:
two people with the same name must remain distinct until the analyst explicitly chooses
association/merge. Index: InvestigationId+NormalizedName. MergedIntoId has a composite
same-investigation self-reference. Retired records remain present, preserving history.
Aliases are preserved from renames and merges; original mention strings never change.

Snapshot insertion/source extraction commits before the HTTP NLP call, without holding
a database transaction over network I/O. Validated candidate insertion, job completion
and its audit event commit together. Optimistic revision checks prevent a cancelled job
from committing late results and prevent stale review/merge updates. Python never writes
these tables. One local API/worker instance owns a database.

SourceState Ready indicates text extraction, not NLP approval or job success. NLP
readiness/failures are represented in ProcessingJobs. Full text snapshots intentionally
consume additional local storage to retain exact evidence across extraction upgrades.
There are no relationship or graph tables in this migration.

## Phase 4 migration: EntityGraph

Adds ProcessingJobs.Kind, default ENTITIES for existing job history, and adds
Relationships and RelationshipEvidence. No graph database or Phase 5 tables are introduced.
Existing entity/source/evidence tables retain their records and constraints.

Relationships stores GUID Id/InvestigationId/SourceEntityId/TargetEntityId, current Type,
review Status, optional CustomLabel, Notes, immutable Origin, optional RetiredReason,
UTC CreatedAt and optimistic Revision. Endpoint composite foreign keys reference
Entities(Id,InvestigationId) with RESTRICT, preventing cross-investigation links.
Indexes cover investigation/status and both endpoints. CHECK constraints bound types/
statuses and disallow self-links unless explicitly retired by an entity merge.

RelationshipEvidence stores GUID Id/RelationshipId/SourceId/SnapshotId/original source
and target mention IDs; UTF-16 Start/End; stable unique OriginKey; Method; review Status.
Foreign keys RESTRICT deletion of referenced relationships, sources, mentions and
snapshots; snapshot/source composite keys enforce source consistency. Span/status
CHECK constraints prevent negative/empty/oversized citations or unknown review states.
Quotes are reconstructed from immutable snapshots, never mutable current extracted text.

Generation atomically adds proposals/passages and completes a persistent source job.
The active-job unique index still allows only one job of either kind per source.
Relationship generation never changes source extraction status; cancellation/retry
preserve the job kind. Existing ENTITIES jobs migrate with their original meaning.

OriginKey hashes snapshot ID + sorted original mention IDs + sentence offsets + method.
Identical reruns do not duplicate citations or reset decisions. New evidence added to
a reviewed co-mention remains proposed until individually reviewed. Approval/rejection,
classification and merge redirection write audit events in the same transaction.

Entity merges redirect relationship endpoints atomically with mention moves and alias
updates. Self-links are retired, not deleted; parallel relationships stay distinct.
No approval or evidence history is silently collapsed. Graph queries exclude retired
and rejected records; detail endpoints retain history/citations for investigation review.

## Phase 5 migration: TimelineGeography

Adds Events (AnalysisEvent), EventEvidence, EventEntities, Locations (LocationProfile),
LocationEvidence and GeographicFocus. All finding/evidence identities are GUIDs; stored
creation/removal timestamps are UTC. DateOnly event bounds are research dates. Event
precision stays separate from interval bounds; UNKNOWN has no calendar values. CHECK
constraints enforce statuses, event types, nonempty titles, date order, coordinate
ranges/precision, positive approximate radii and citation spans. Revisions protect review,
correction, evidence changes and merging. Existing Phase 4 records remain unchanged.

Event/location composite keys and foreign keys enforce investigation-scoped entity and
location associations. EventEntities is unique by event/entity. Locations has a filtered
unique index for one active profile per entity. Event origin keys are unique across
reruns; soft deletion leaves those keys intact. Event job provenance references the
persistent ProcessingJob. EVENTS needs no job table rebuild: Kind already stores strings.

Evidence uniquely identifies parent/snapshot/start/end, references the original
SourceTextSnapshot through snapshot/source composite keys and retains Active=false
rows when citations are removed. Quotes always derive from the immutable snapshot.
Finding approval requires active evidence at the application/domain boundary; all
mutations and corresponding audit entries save atomically. Finding/evidence FKs use
RESTRICT. Removed findings remain in the database, keeping original dates, reasons,
links and citations. No finding or source is hard-deleted by these review APIs.

GeographicFocus is an investigation-keyed viewport preference (latitude/longitude/zoom)
with range constraints. Only this view preference cascades when an otherwise empty
investigation is deleted. It is not an assertion of geographic truth and needs no citation.
