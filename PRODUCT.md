# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users

Primary: portfolio reviewers such as hiring managers and engineers. They usually meet
ARGUS through the README screenshots, the fictional Operation Meridian demo and a short
local run. In a few minutes they need to see real full-stack, NLP, graph and analytical
depth, and see that the tool handles uncertainty honestly.

Secondary, and the user the product is modeled on: an OSINT analyst or investigative
researcher. This person collects public-source documents, reviews machine-proposed
entities and relationships, rebuilds timelines and places, and writes assessments
backed by evidence. Everything ARGUS shows must still make sense as this person's
real workflow. The portfolio audience judges the tool by how believable that
workflow is.

## Product Purpose

ARGUS is a local-first OSINT analysis workstation. It turns unstructured public-source
documents into a structured, traceable analytical workspace, and it never overstates
how reliable automatically extracted findings are. Success means a reviewer can follow
the whole chain: collect documents, review entities, explore connections, reconstruct
events, assess evidence, export a report and back up the workspace. At every step,
source observation, analyst interpretation and unresolved contradiction stay visibly
separate.

## Positioning

ARGUS is an analyst's workstation, not a news aggregator or an AI chatbot. Its
distinguishing mechanism is human review over deterministic, local processing.
Proposals stay proposals until approved, every claim cites exact snapshot passages
with original hashes, contradictions stay unresolved instead of being smoothed over,
and evidence never leaves the machine. Nothing needs a paid API, hosted LLM or network
connection for normal analysis.

## Operating Context

- Single user on Windows. Three local services: Blazor web app (localhost:5200),
  ASP.NET Core API (5100) and the Python FastAPI/spaCy NLP service (8000). All are
  bound to loopback, with no authentication.
- The workspace is a SQLite database plus a managed document directory, backed up
  and restored as a checksummed unit.
- Investigation-scoped navigation: Overview, Sources, Entities, Relationship Graph,
  Timeline, Geospatial Map, Assessments, Reports. Processing Jobs and Workspace
  Backup are global.
- The demo is loaded through the real import, NLP and review APIs by
  `scripts/load_demo.py`. The Meridian smoke test regenerates the README screenshots
  in `docs/screenshots/`.

## Capabilities and Constraints

- Confirmed features are listed in README "What works": preserved sources with hashes
  and reliability, local NER with review states, co-mention relationship proposals,
  graph metrics and paths, dated or unresolved events, uncertainty-bounded geography,
  claims with supporting, contradictory and context passages, approved-only Markdown
  reports, backup and restore.
- Terminology carries meaning: proposed, approved and rejected; reliability (of a
  source) versus confidence (of a claim); supporting, contradictory and context
  evidence. Approval records review, not independent verification.
- Confidence and reliability are analyst judgments, never computed probabilities.
  Structural associations never imply wrongdoing, intent or threat.
- Ambiguous or relative dates stay unresolved and never receive invented dates.
  Online OSM map tiles require an explicit opt-in. The offline map uses a coordinate
  grid.
- No placeholder controls that look functional but do nothing. Every surface needs
  real loading, empty, validation, error and success states.
- Stack is fixed: Blazor Web App (.NET 10) with Cytoscape.js and Leaflet. Web
  references Core contracts and calls the API. Business rules stay out of components.
  Development follows the authorized phase in `docs/development-plan.md`.

## Brand Commitments

- The name is ARGUS (Analytical Research, Graphs, and Unified Sources).
- Voice is precise, sober and evidentiary. Copy states limits plainly (see README
  disclaimers) and never dramatizes.
- Binding semantic color meanings: amber marks warnings and uncertain findings,
  restrained red marks errors and contradictory evidence, neutral gray marks secondary
  information.
- No decorative military graphics, surveillance or targeting motifs, and no
  "hacker" neon.
- The current dark navy/charcoal palette with muted cyan is the incumbent starting
  point, not a binding identity. A future redesign may replace it.

## Evidence on Hand

- Operation Meridian: a fully fictional training exercise with 12 documents,
  3 organizations, 6 people, 5 places, 10 events, 15 proposed and 3 reviewed
  relationships, 5 claims and 2 deliberately unresolved conflicts. All names and
  hypothetical map points are fabricated. Data lives in `sample-data/`; loader is
  `scripts/load_demo.py`.
- Real application captures: `docs/screenshots/meridian-*.png` (graph, map, overview,
  timeline, assessments, report).
- No users, testimonials, deployments or benchmarks exist. Future work must not
  invent any. Demo content must stay clearly fictional and must never use real people
  or organizations.

## Product Principles

1. Show the chain of custody. Any finding should trace back to its source passage
   and hash within a click or two.
2. Keep uncertainty in view. Proposed, unresolved, contradictory and low-confidence
   states are first-class and visually distinct, never hidden for a cleaner look.
3. Legible depth in minutes. A reviewer should grasp the workflow and its rigor from
   the demo without a guided tour, while the tool stays honest for real analysis.
4. Human judgment decides. The machine proposes and the analyst approves, and the
   interface never implies automated verification.
5. Local and inspectable. Nothing silently reaches the network, and every result is
   reproducible from the workspace.

## Accessibility & Inclusion

No formal standard is targeted. Best effort: accessible labels on forms and controls
(per the spec), keyboard-operable controls, and meaning that never depends on color
alone, since status colors carry analytical semantics.
