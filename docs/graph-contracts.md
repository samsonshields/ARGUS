# Phase 4 graph and relationship contracts

All application endpoints use `/api/v1`. .NET owns persistence and all review decisions;
Python returns stateless sentence-co-occurrence candidates only. No graph database is used.

| Method | Route | Behavior |
| --- | --- | --- |
| POST | /sources/{id}/process-relationships | 202 persistent RELATIONSHIPS job; 409 if source already busy |
| POST | /investigations/{id}/relationships/generate | 202 `{jobs,busySources}`; eligible sources have at least two approved entities |
| GET | /investigations/{id}/relationships?status=&type=&page=1&pageSize=50 | paginated active proposals/reviewed/rejected relationships |
| GET | /entities/{id}/relationships?page=1&pageSize=50 | paginated incident relationships |
| GET | /relationships/{id}?evidencePage=1 | detail and 20 cited passages per page, including retained/retired history |
| POST | /relationships/{id}/approve | explicit evidence review and optional proposal classification |
| POST | /relationships/{id}/reject | reject active PROPOSED relationship; retain citations/history |
| GET | /investigations/{id}/graph?entityType=&relationshipType=&includeProposed=true&search= | bounded graph and structural metrics |
| GET | /investigations/{id}/graph/path?sourceId=&targetId=&respectDirection=false | shortest path under the same optional graph filters |

Collections use the existing PageResult contract; evidence details include evidenceTotal,
evidencePage and evidencePageSize. Invalid enum/pagination inputs return 400; unknown
investigations/records/path endpoints return 404; review/queue conflicts return 409.
Graphs over 2000 nodes or 10000 edges return 413 rather than silently truncating.
Graph name filtering limits both nodes and their connecting edges; relationship filtering
can leave isolated nodes. Empty graphs and disconnected paths are valid results.

RelationshipSummary identifies investigation, current canonical endpoints/names, type,
status, optional customLabel, analyst notes, immutable origin, optional retiredReason,
approvedEvidenceCount and proposedEvidenceCount. Detail adds evidence IDs, source title/
sourceId/snapshotId, original mention IDs, exact UTF-16 passage bounds/quote/textHash,
evidence review status and generation method. Evidence quotes derive from immutable snapshots.

Approve JSON:

```json
{
  "type": "MENTIONED_WITH",
  "customLabel": null,
  "notes": "",
  "reverse": false,
  "confirmEvidenceReviewed": true,
  "evidenceIds": ["<evidence GUID belonging to this relationship>"]
}
```

Types: MEMBER_OF, ASSOCIATED_WITH, LOCATED_IN, PARTICIPATED_IN, MENTIONED_WITH, CUSTOM.
Substantive types need nonempty analyst notes (max 2000); CUSTOM also needs a label
(max 100). MEMBER_OF, LOCATED_IN, PARTICIPATED_IN and CUSTOM are directed; others
are undirected. Proposal review can reverse direction. Already approved classifications/
directions cannot be silently replaced. Review requires 1–100 selected passages and
explicit confirmation. New evidence on a reviewed co-mention remains PROPOSED; it is
not automatically treated as approved support. Rejected proposals are not reset by reruns.

Graph nodes include id/label/type/degree/connectionCount/degreeCentrality/betweenness/
component. Edges include id/source/target/type/status/label/directed/evidenceCount/
pendingEvidenceCount. Nodes are active analyst-approved canonical entities, including
isolates. Default graph includes proposed edges; includeProposed=false shows only
reviewed edges with reviewed evidence. Rejected and merge-retired edges never appear.

Metrics use an unweighted undirected simple projection (parallel edges collapse for
unique-neighbor degree and centrality). ConnectionCount counts all incident edges.
Degree centrality is degree/(N-1). Normalized Brandes betweenness uses the full filtered
graph's N, including disconnected components. Components are zero-indexed structural
groups. Path response is `{found,nodeIds,edgeIds}`; disconnected endpoints return
found=false, empty lists. Direction can be respected independently of metric semantics.
None of these metrics measures real-world danger, intent or truth.

## Python relationship pipeline v1

POST `/process/relationships` accepts the entity contract's contractVersion/sourceId/
textHash/text plus `mentions`: approved mention id/entityId/text/start/end. The API
derives these from SQLite; they are not unreviewed NER suggestions. Python validates
hashes, exact UTF-16 spans, Unicode boundaries and unique mention IDs. The English
spaCy sentencizer is deterministic and needs no trained NER model for this endpoint.

Response echoes version/source/hash, offsetConvention=UTF16_CODE_UNITS,
method=sentence_cooccurrence_v1, and `relationships`. Each candidate includes original
sourceMentionId/targetMentionId, canonical sourceEntityId/targetEntityId, type=MENTIONED_WITH,
exact sentence start/end/quote, confidence=null. Same-entity mentions do not form a
self-edge. One earliest mention per entity per sentence determines stable evidence.
No semantic relationship, probability, date or location is inferred.

.NET independently validates candidate IDs/types/spans/quotes against its approved
input and immutable snapshot before staging writes. Jobs use the existing retry/cancel/
restart lifecycle. Entire source-generation results and completion commit atomically.
Stable evidence keys include snapshot, original mention pair, sentence bounds and
method. Identical reruns preserve reviews. Concurrent entity changes trigger bounded
retry; merges redirect endpoints but preserve citations/review history. Self-links
after a merge are retired; parallel relationships remain separate.

Limits: 50 snapshots/source job, 5000 mentions/snapshot, 5000 candidate passages/job,
8000 UTF-16 units/citation, 120-second total generation deadline and existing 30-second
per-HTTP timeout/4 MiB response cap. No partial/truncated candidate set is stored.
Python errors use the existing sanitized problem contract and do not echo documents.
There is no `/process/events`, map, timeline or geographic processing in Phase 4.
