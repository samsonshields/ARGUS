# Database schema (Phases 1 and 2)

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
