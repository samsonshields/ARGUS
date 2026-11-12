# Phase 6: assessments and Markdown reports

All paths below extend `/api/v1`. Python remains stateless and unchanged: Phase 6
claims are analyst-authored, and report generation is deterministic .NET code. It
does not call an LLM, infer confidence, or depend on the NLP service being online.
No Phase 7 backup, performance project, demo dataset or portfolio work is included.

## Claims

| Method/path | Behavior |
| --- | --- |
| GET `/investigations/{id}/claims` | Paginated active claims |
| POST `/investigations/{id}/claims` | Create a proposal |
| GET/PUT `/claims/{id}` | Detail / correction |
| POST `/claims/{id}/evidence` | Attach, reactivate or relabel a preserved passage |
| DELETE `/claims/{id}/evidence/{evidenceId}?revision=...` | Retire an evidence association |
| POST `/claims/{id}/approve`, `/reject` | Explicit assessment review |
| POST `/claims/{id}/remove` | Confirmed soft removal |
| GET `/investigations/{id}/assessments/summary` | Review and unresolved-contradiction counts |

List filters: status PROPOSED/APPROVED/REJECTED, confidence, unresolvedOnly, statement
search (<=200), page >=1, pageSize 1-100 (default 20). Omitted status/confidence means
all. Removed claims stay in storage/audit history but leave active lists/detail APIs.

Edit body: statement (1-2000), reasoning (<=10000), confidenceLevel, contradictionsResolved,
contradictionResolution (<=5000), revision. Confidence is LOW, MODERATE, HIGH or
INSUFFICIENT_INFORMATION, defaulting to INSUFFICIENT_INFORMATION. Confidence reflects
the analyst's judgment, never an automatic probability or source-reliability conversion.
Statements are explicitly presented as analytical claims; reasoning as interpretation;
quoted passages as source observations. Approval never means independent verification.

Evidence body: snapshotId, start, end, relationship, notes (<=2000), revision. Relationship
is SUPPORTS, CONTRADICTS or CONTEXT. A claim retains up to 100 distinct passages; each
is uniquely identified by claim/snapshot/start/end. Posting that same tuple updates its
label/note or reactivates it. Document/snapshot/offset identity does not change. Exact
nonempty quotes are <=8000 UTF-16 units with valid surrogate boundaries and must belong
to the same investigation. Source reliability A-E is displayed with every citation.
It is a manual source grade, not proof that the cited statement is true.

The existing `/sources/{id}/passages` snapshot picker supplies source-backed chunks.
APIs also accept precise subranges. All quotation slicing happens in .NET: SQLite
substring character indexes differ from the persisted UTF-16 offset convention when
text contains surrogate pairs. Snapshot text/hash and original document identities stay
immutable. Removal retires associations, retaining citation and audit history.

Corrections and all evidence additions/removals/relabels return the claim to PROPOSED.
Evidence changes also clear any previous contradiction resolution. A resolved decision
requires active contradictory evidence and an explanation; unresolved reasoning can
instead remain in the reasoning field. Both resolved and unresolved contradictory
passages remain attached and appear in reports. No conflict is silently resolved.

Review body: `{ "confirm": true, "acknowledgeUnresolvedContradictions": true,
"revision": 4 }`. Approval requires at least one SUPPORTS passage, nonempty explanatory
reasoning, valid snapshot provenance and explicit acknowledgment when contradictions
remain unresolved. A claim with INSUFFICIENT_INFORMATION can still be reviewed if
support and reasoning explain that uncertainty. Rejection does not erase evidence.
Removal uses `{ "confirm": true, "revision": 4 }`.

Every edit/review/evidence mutation checks the current revision and uses an optimistic
concurrency token. Stale writes return 409. Validation returns 400, missing records 404,
limits 422. Claim/confidence/evidence changes save audit before/after state atomically.
The UI disables review/evidence edits while a content draft is unsaved.

## Reports

| Method/path | Behavior |
| --- | --- |
| GET `/investigations/{id}/reports/validation` | Validate current approved material; no artifact written |
| POST `/investigations/{id}/reports` | Validate again and persist an immutable Markdown snapshot |
| GET `/investigations/{id}/reports?page=1` | Saved report history (20/page) |
| GET `/reports/{id}` | Markdown, SHA-256, generated UTC timestamp and generation validation |
| GET `/reports/{id}/markdown` | UTF-8 `.md` attachment |

Generate body: `{ "confirmApprovedContentReviewed": true }`. A valid report needs at
least one approved active evidence-backed claim. The validation response contains
isValid, errors, warnings and included claim/entity/event/location/source/passage counts.
Blocked generation returns 422 without a partial report/audit artifact. Confirmation
is mandatory; an earlier validation result cannot bypass generation-time validation.

Only approved, active claims/events/locations and active canonical entities with
approved mentions are included. Proposed/rejected/removed findings are excluded.
Every included finding needs exact evidence. Reference validation checks source scope,
snapshot/source identity, UTF-16 bounds, snapshot SHA-256 and reconstructed quotations.
Original files must exist, match stored sizes and match their original SHA-256. Invalid
dates/coordinate precision or unavailable originals block generation. Source reliability
and metadata are recorded as they existed at generation time. Irrelevant library sources
and unapproved source passages are not added merely because they exist in the database.

The ten sections are Executive Summary, Scope and Research Questions, Key Findings,
Relevant Entities, Chronological Events, Geographic Context, Source Evaluation,
Conflicting Information, Analytical Limitations and References. Empty optional modules
receive explicit insufficient-information text. Partial/unknown dates and approximate/
unknown geography retain their precision. Scope fields are labeled analyst-provided.
Confidence, supporting/contradictory/context labels, reasoning, conflict explanations
and unresolved warnings remain visible. Associations alone never establish wrongdoing.

Each factual finding references a generated P-number anchor. Reference entries contain
the exact quotation, source title/ID, local ARGUS source route, snapshot ID/hash, original
hash, retrieval time and exact UTF-16 offsets. Attributed URLs are text and are not fetched.
User text is HTML-encoded and Markdown-escaped; generated static anchors are the only
raw HTML. The UI shows an encoded Markdown preview and offers an attachment, without
executing report HTML. The smoke test independently renders exports with CommonMark
and checks anchor targets and malicious HTML/link escaping.

Database reads, final validation, artifact insertion and audit run in one SQLite
transaction. Reports retain a GUID, UTC generation time, UTF-8 content hash, Markdown
and generation validation. Reports have no mutation endpoint; corrections create new
reports. Existing reports remain byte-identical after claim edits/removal/restarts and
are labeled snapshots of approval at generation time, not guarantees of current review.
Downloads independently verify artifact hash integrity before returning content.

Bounds: 500 approved findings per optional module or claims, 2000 total evidence
associations and 2000 distinct passages, 200 sources, 16,000,000 UTF-16 units across
referenced snapshots, 256 MiB total originals, 30-second original-verification deadline,
100 MiB maximum individual original, 8 MiB UTF-8 Markdown. Exceeding a bound blocks
export rather than truncating findings. This remains one local API/worker per database.
Web downloads proxy API attachments; Web has no SQLite/document storage dependency.

Markdown export is analytical reporting, not a full investigation backup. PDF and
editable HTML report output are outside Phase 6. Phase 7 remains unimplemented.
