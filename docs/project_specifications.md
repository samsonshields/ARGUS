# Project Proposal: ARGUS
## Open-Source Intelligence Analysis & Investigation Workstation

**Project Type:** Full-stack intelligence analysis platform  
**Working Name:** ARGUS — Analytical Research, Graphs, and Unified Sources  
**Target Audience:** Intelligence analysts, investigative researchers, cybersecurity analysts, and OSINT professionals  
**Development Scope:** Solo developer, modular portfolio project  
**Primary Languages:** C#, Python, TypeScript, SQL  
**Target Platform:** Local-first web application running on Windows  
**Development Environment:** Visual Studio Code, .NET SDK, Python virtual environment, Docker (optional)

---

# 1. Executive Summary

ARGUS is a modular Open-Source Intelligence (OSINT) analysis workstation designed to assist researchers in collecting, organizing, evaluating, and visualizing publicly available information.

The application will transform unstructured information, such as news articles, public reports, and research documents, into structured analytical data.

Users will be able to create investigations, ingest documents, extract entities, identify potential relationships, construct timelines, visualize geographic events, and create evidence-backed intelligence assessments.

The system will emphasize **traceability, source reliability, analytical transparency, and human review**.

ARGUS should resemble a professional intelligence analysis environment rather than a conventional news aggregator or generic AI chatbot.

## 1.1 Primary Objectives

1. Develop a functional full-stack intelligence analysis application.
2. Implement automated ingestion and processing of public information.
3. Extract structured entities and relationships from unstructured documents.
4. Construct interactive entity relationship graphs.
5. Provide geographical and chronological visualizations.
6. Implement source attribution, evidence tracking, and analytical confidence assessments.
7. Demonstrate a maintainable modular architecture with comprehensive automated testing.
8. Operate using publicly available, synthetic, or user-provided information without requiring paid intelligence APIs.

## 1.2 Non-Goals

The initial system will not attempt to provide classified intelligence functionality, covert collection, private-account monitoring, biometric identification, or automated determinations that a person is dangerous or involved in wrongdoing.

ARGUS is intended as an analytical research tool. Its outputs must distinguish evidence from inference.

---

# 2. Technology Stack

The application will use a layered architecture separating user interface rendering, business logic, intelligence processing, and persistence.

## 2.1 Frontend

**Framework:** ASP.NET Core Blazor Web App (.NET 10 LTS)

**Component Library:** MudBlazor, subject to compatibility verification.

**Visualization Libraries:**
- Cytoscape.js for interactive relationship graphs.
- Leaflet.js for geographic mapping.
- A Blazor-compatible chart library or lightweight JavaScript charting library for statistics.
- Custom CSS for consistent application styling.

**Responsibilities:**
- Investigation management.
- Document browsing.
- Relationship graph exploration.
- Timeline interaction.
- Geographic visualization.
- Analyst review workflows.
- Report generation interfaces.

## 2.2 Application Backend

**Language:** C#

**Framework:** ASP.NET Core Web API

**Responsibilities:**
- REST API endpoints.
- Investigation and user-facing business logic.
- Database operations.
- Input validation.
- Document metadata management.
- Evidence association.
- Audit logging.
- Communication with the Python intelligence-processing service.

Use Entity Framework Core for ORM functionality and migrations.

## 2.3 Intelligence Processing Service

**Language:** Python 3.12+

**Framework:** FastAPI

**Libraries:**
- spaCy for named entity recognition.
- NetworkX for graph analysis.
- scikit-learn for optional document clustering.
- Pydantic for request/response validation.
- pytest for automated testing.

**Responsibilities:**
- Text normalization.
- Named entity extraction.
- Candidate entity resolution.
- Relationship candidate extraction.
- Document similarity analysis.
- Timeline candidate extraction.
- Explainable analytical scoring.

Initial processing must work without paid APIs, external LLMs, or remote AI services.

Advanced LLM-powered features may be implemented as optional extensions after the core application is functional.

## 2.4 Persistence

**Primary Database:** SQLite

Use a relational schema with Entity Framework Core migrations.

The system should provide abstractions that allow a future transition to PostgreSQL.

Graph data should initially be represented using relational tables. A dedicated graph database is not necessary for the MVP.

**Document Storage:** Local application-controlled directory.

Store metadata and references in SQLite. Store original documents in a managed filesystem location.

---

# 3. System Architecture

The system will consist of three principal components:

1. Blazor Web Application
2. ASP.NET Core Application API
3. Python Intelligence Processing Service

The browser communicates with the ASP.NET Core API.

The ASP.NET Core API owns all database writes and application business logic.

The Python service performs stateless processing and returns structured candidate results to the .NET application.

The Python service must not independently modify the application database.

## 3.1 Data Flow

```text
USER
  |
  v
BLAZOR WEB APPLICATION
  |
  | HTTP / JSON
  v
ASP.NET CORE API
  |
  +----> SQLite Database
  |
  +----> Document Storage
  |
  +----> Python FastAPI Service
                |
                +--> Text Processing
                +--> Entity Extraction
                +--> Relationship Candidates
                +--> Event Extraction
                +--> Similarity Analysis
                |
                v
         Structured Results
                |
                v
         ASP.NET CORE API
                |
                v
          Analyst Review
                |
                v
        Approved Data Storage
```

## 3.2 Architectural Rules

- The frontend must never access SQLite directly.
- Python processing must remain independent of the frontend.
- All authoritative database writes must pass through the .NET API.
- Data transfer between C# and Python must use explicitly defined JSON contracts.
- Processing failures must not corrupt investigations or delete source documents.
- Automatically extracted information must be labeled as proposed until reviewed.
- Approved findings must preserve provenance to the underlying source material.
- The system must remain functional without internet access when working with existing documents.

---

# 4. Core Functional Modules

## Module A: Investigation Management

### Purpose

Provide organized research workspaces in which documents, entities, events, and analytical findings can be grouped.

### Features

- Create investigations.
- Edit investigation metadata.
- Archive investigations.
- Assign investigation titles and descriptions.
- Define geographical and chronological scope.
- Display investigation activity statistics.
- Search and filter investigations.
- Open investigation-specific analytical views.

### Investigation Metadata

```text
Investigation
-------------
Id
Title
Description
Status
CreatedAt
UpdatedAt
StartDate
EndDate
Region
```

### Acceptance Criteria

An analyst must be able to create a new investigation, reopen it after restarting the application, modify its details, and view its associated information.

Deleting investigations should be a deliberate confirmed operation, with dependent records handled according to explicit database constraints.

---

## Module B: Document Ingestion & Source Management

### Purpose

Allow analysts to import, organize, search, and assess open-source information.

### MVP Supported Inputs

- Plain text files (.txt)
- Extractable PDF documents (.pdf)
- Directly pasted text
- Public article URLs (optional after local ingestion works)

### Features

- Upload documents to an investigation.
- Extract and normalize document text.
- Preserve the original source file.
- Calculate a SHA-256 hash for uploaded content.
- Detect exact duplicate documents.
- Maintain source metadata.
- Search documents by title, content, and date.
- Reprocess documents after pipeline upgrades.
- Display processing state and errors.

### Source Metadata

```text
Source
------
Id
InvestigationId
Title
SourceType
OriginalUrl
Publisher
PublishedAt
RetrievedAt
FilePath
ContentHash
ExtractedText
ProcessingStatus
```

### Reliability Assessment

Allow an analyst to enter source reliability using a five-level categorical scale:

- A — Generally reliable
- B — Usually reliable
- C — Mixed or uncertain reliability
- D — Frequently unreliable
- E — Insufficient information

The rating describes the assessed source, not whether any specific claim is true.

An additional `InformationCredibility` field may represent the credibility of individual claims separately.

Reliability assessments must initially be manually assigned. Automated systems must not present their assessments as verified truth.

### Important Processing Constraints

- Set configurable file size limits.
- Reject unsupported file types.
- Sanitize filenames.
- Prevent path traversal.
- Avoid executing uploaded content.
- Distinguish PDF text extraction from OCR.
- Do not include OCR in the MVP.
- Use timeouts and clear error handling for document processing.

---

## Module C: Automated Named Entity Recognition

### Purpose

Extract potentially relevant names, organizations, locations, and dates from documents.

### Supported Entity Types

```text
PERSON
ORGANIZATION
LOCATION
DATE
EVENT
OTHER
```

### Example Input

> "On March 12, a United Nations delegation met with representatives in Geneva to discuss regional security."

### Example Output

```json
{
  "entities": [
    {
      "text": "United Nations",
      "type": "ORGANIZATION",
      "start": 15,
      "end": 29,
      "confidence": null
    },
    {
      "text": "Geneva",
      "type": "LOCATION",
      "start": 69,
      "end": 75,
      "confidence": null
    }
  ]
}
```

Offsets must follow one documented indexing convention and must be tested against the original text.

The example is illustrative; actual extraction results depend on the NLP model.

A null confidence value indicates that the model does not provide a calibrated probability for that result. Do not fabricate confidence percentages.

### Features

- Extract recognized entities.
- Highlight entity mentions in documents.
- Store mention locations and source references.
- Associate multiple mentions with a canonical entity.
- Suggest possible duplicate entities.
- Permit manual entity merging.
- Allow analysts to correct entity classifications.
- Preserve original mention text after corrections.

### Entity Resolution

The system should initially use deterministic rules:

1. Normalize whitespace and case.
2. Compare canonical names.
3. Compare known aliases.
4. Suggest fuzzy matches above a configurable threshold.
5. Require analyst confirmation for merges.

The application must not automatically merge two people solely because their names are similar.

### Acceptance Criteria

Processing an imported document must generate reviewable entity candidates.

Approved entities must appear in the investigation's entity list.

The analyst must be able to inspect the exact source passage from which each entity mention originated.

---

## Module D: Relationship Analysis & Graph Visualization

### Purpose

Visualize relationships between entities and help analysts explore evidence-backed connections.

### Graph Model

Represent entities as nodes and relationships as edges.

**Node Types:**
- Person
- Organization
- Location
- Event
- Other

**Relationship Types:**
- MEMBER_OF
- ASSOCIATED_WITH
- LOCATED_IN
- PARTICIPATED_IN
- MENTIONED_WITH
- CUSTOM

### Relationship Rules

Co-occurrence in a document is not proof of a substantive relationship.

`MENTIONED_WITH` may be generated as a low-assertion analytical connection, but it must be visually distinguished from a confirmed, evidence-backed relationship.

Automated relationship extraction initially generates candidates only.

### Graph Features

- Pan and zoom.
- Select nodes.
- Inspect associated evidence.
- Filter by entity type.
- Filter by relationship type.
- Display connection counts.
- Search for specific nodes.
- Expand neighboring entities.
- Highlight selected paths.
- Distinguish proposed and approved edges.
- Display citations for each relationship.

### Graph Algorithms

Implement or integrate:

**Degree Centrality:** Identify highly connected nodes.

**Connected Components:** Identify separate clusters.

**Shortest Path:** Determine graph connection paths between entities.

**Betweenness Centrality:** Identify nodes linking otherwise separate sections of a graph.

These metrics describe graph structure, not the importance, intent, or threat level of a real person.

### Acceptance Criteria

A graph of at least 100 nodes and 300 edges must remain usable on a standard development computer.

Selecting an edge must reveal its relationship type, review status, and supporting document passage.

The graph must handle disconnected components and empty investigations gracefully.

---

## Module E: Chronological Intelligence Timeline

### Purpose

Allow analysts to reconstruct sequences of events and compare claims across sources.

### Features

- Extract event date candidates.
- Create events manually.
- Associate events with entities.
- Associate events with source passages.
- Display events chronologically.
- Filter events by type and investigation.
- Navigate from timeline entries to source documents.
- Distinguish exact dates from approximate dates.
- Allow corrections and removal of events.

### Event Model

```text
Event
-----
Id
InvestigationId
Title
Description
StartAt
EndAt
DatePrecision
LocationId
Status
CreatedAt
```

`DatePrecision` should support exact dates, month-only dates, year-only dates, and unknown dates.

Relative phrases such as "last Tuesday" must not be resolved without a reliable reference date.

### Acceptance Criteria

Approved events must display in chronological order.

Clicking an event must show the supporting evidence.

Changes to an event date must update the timeline without requiring the user to restart the application.

---

## Module F: Geospatial Intelligence Viewer

### Purpose

Display geographic locations and events associated with an investigation.

### Technology

Leaflet.js with appropriate mapping tiles.

Use OpenStreetMap-compatible tile providers with their usage policies respected.

A configured local or offline map option may be added later.

### Features

- Display location entities on a map.
- Display event markers.
- Filter markers by date and type.
- Open details from map markers.
- Navigate between maps and entity records.
- Display uncertainty for approximate locations.
- Support an investigation-level geographic focus.

### Geolocation Rules

Only use coordinates derived from verified source metadata or explicitly selected locations.

Do not infer exact coordinates from vague references.

For example, a reference to "Northern Syria" must not be represented as a precise building-level coordinate.

Do not automatically geocode private residential addresses.

### Acceptance Criteria

The map must display approved location entities with known coordinates.

Selecting a marker must open relevant investigation information and citations.

---

## Module G: Evidence & Analytical Assessment

### Purpose

Enable analysts to assemble structured assessments while preserving the distinction between observations, interpretations, and conclusions.

### Features

- Create analytical claims.
- Attach supporting evidence.
- Attach contradictory evidence.
- Record analyst confidence.
- Enter explanatory reasoning.
- Track claim review status.
- Identify unresolved contradictions.
- Generate assessment summaries.

### Claim Model

```text
Claim
-----
Id
InvestigationId
Statement
ConfidenceLevel
Status
Reasoning
CreatedAt
UpdatedAt
```

### Confidence Levels

```text
LOW
MODERATE
HIGH
INSUFFICIENT_INFORMATION
```

Confidence represents the analyst's assessment of the evidence, not a calculated probability of truth.

### Evidence Relationships

A claim may reference multiple sources.

Each source reference must identify the specific document and supporting passage.

Evidence must include a relationship to the claim:

```text
SUPPORTS
CONTRADICTS
CONTEXT
```

### Example

**Claim:** A regional organization held a diplomatic meeting on a specified date.

**Supporting evidence:** A dated official press release.

**Contradictory evidence:** A separate report listing a different meeting date.

**Confidence:** Moderate.

**Analytical note:** The event is supported by multiple sources, but the published dates conflict.

### Acceptance Criteria

An analyst must be able to create a claim, attach multiple evidence records, label supporting and contradictory passages, and save a reviewable assessment.

An assessment must never be described as independently verified merely because it appears in the database.

---

## Module H: Intelligence Report Generation

### Purpose

Produce structured, evidence-backed research reports from an investigation.

### MVP Output Format

Markdown.

PDF export may be introduced in a subsequent phase.

### Report Sections

1. Executive Summary
2. Scope and Research Questions
3. Key Findings
4. Relevant Entities
5. Chronological Events
6. Geographic Context
7. Source Evaluation
8. Conflicting Information
9. Analytical Limitations
10. References

### Rules

- Include only analyst-approved findings in finalized reports.
- Every substantive factual claim must reference evidence.
- Clearly label analyst interpretation.
- Never create fictional citations.
- State when information is insufficient.
- Do not assert that an investigation demonstrates wrongdoing based solely on associations.
- Include the report generation date.

### Acceptance Criteria

The application must be capable of generating a Markdown report from an investigation with approved claims and sources.

The report must contain valid source references and render correctly even if optional modules contain no data.

---

# 5. Database Design

Use the following relational entities as the baseline schema.

| Table | Purpose |
|---|---|
| Investigations | Research workspaces |
| Sources | Imported documents and metadata |
| Entities | Canonical people, organizations, locations, etc. |
| EntityMentions | Source-specific entity appearances |
| Relationships | Connections between entities |
| RelationshipEvidence | Supporting source passages |
| Events | Dated activities or developments |
| EventEvidence | Evidence linked to events |
| Claims | Analytical propositions |
| ClaimEvidence | Supporting, contradicting, or contextual passages |
| ProcessingJobs | Asynchronous processing state |
| AuditEvents | Recorded data modifications |

## 5.1 Data Integrity Requirements

All primary keys should use UUIDs or GUIDs consistently.

Use foreign-key constraints.

Use indexes for:

- Investigation IDs.
- Entity names.
- Source hashes.
- Event timestamps.
- Relationship endpoints.
- Processing status.

Store UTC timestamps for system-generated dates.

Allow nullable values where information is genuinely unknown.

Store proposed analytical records separately by status rather than treating them as automatically approved.

Preserve source provenance when correcting, merging, or deleting analytical records.

Implement appropriate delete restrictions and cascading behavior explicitly through migrations.

## 5.2 Auditability

Record significant changes to:

- Sources
- Entity merges
- Relationships
- Events
- Claims
- Confidence assessments

An audit event should contain:

```text
Id
InvestigationId
RecordType
RecordId
Action
BeforeJson
AfterJson
OccurredAt
Actor
```

For the MVP, `Actor` can identify the local application user. Do not imply a secure multi-user identity system exists until authentication is implemented.

---

# 6. User Interface Requirements

The UI should resemble a modern professional analytical workstation.

## 6.1 Visual Design

**Theme:** Dark, high-contrast analytical interface.

**Colors:**
- Dark navy and charcoal backgrounds.
- Muted cyan accents for interactive controls.
- Amber for warnings and uncertain findings.
- Restrained red for errors and contradictory evidence.
- Neutral gray for secondary information.

Avoid excessive neon effects and decorative military graphics.

Prioritize readability, information density, and clear source attribution.

Use consistent typography, spacing, iconography, and panel styles.

## 6.2 Navigation Structure

```text
ARGUS
|
+-- Dashboard
|
+-- Investigations
|     |
|     +-- Overview
|     +-- Sources
|     +-- Entities
|     +-- Relationship Graph
|     +-- Timeline
|     +-- Geospatial Map
|     +-- Assessments
|     +-- Reports
|
+-- Processing Jobs
|
+-- Settings
```

## 6.3 Dashboard

Display:

- Total investigations.
- Total processed documents.
- Entities awaiting review.
- Approved relationships.
- Recent activity.
- Processing failures.

## 6.4 Investigation Workspace

Use a consistent investigation-level navigation system with a selected investigation context.

The interface should support:

- Loading states.
- Empty states.
- Validation messages.
- Processing errors.
- Success confirmations.
- Responsive layouts.

The UI must not display placeholder buttons that appear functional but perform no action.

---

# 7. API Contract

The ASP.NET Core API will expose versioned REST endpoints.

Base route:

`/api/v1`

## 7.1 Investigation API

```http
GET    /api/v1/investigations
POST   /api/v1/investigations
GET    /api/v1/investigations/{id}
PUT    /api/v1/investigations/{id}
DELETE /api/v1/investigations/{id}
```

## 7.2 Sources API

```http
GET  /api/v1/investigations/{id}/sources
POST /api/v1/investigations/{id}/sources
GET  /api/v1/sources/{sourceId}
POST /api/v1/sources/{sourceId}/process
```

## 7.3 Entities API

```http
GET  /api/v1/investigations/{id}/entities
GET  /api/v1/entities/{entityId}
PUT  /api/v1/entities/{entityId}
POST /api/v1/entities/{entityId}/approve
POST /api/v1/entities/merge
```

## 7.4 Graph API

```http
GET /api/v1/investigations/{id}/graph
GET /api/v1/entities/{entityId}/relationships
```

Graph response:

```json
{
  "nodes": [
    {
      "id": "entity-guid",
      "label": "Example Organization",
      "type": "ORGANIZATION",
      "status": "APPROVED"
    }
  ],
  "edges": [
    {
      "id": "relationship-guid",
      "source": "entity-guid-1",
      "target": "entity-guid-2",
      "type": "ASSOCIATED_WITH",
      "status": "PROPOSED"
    }
  ]
}
```

## 7.5 Additional API Groups

Provide consistent CRUD and review operations for:

- Events
- Claims
- Claim evidence
- Reports
- Processing jobs

Implement Swagger/OpenAPI documentation.

Use appropriate HTTP status codes.

Return structured validation errors, including machine-readable error codes.

Use pagination for collection endpoints.

---

# 8. Python Processing Service

The Python application must provide the following initial endpoints:

```http
GET  /health
POST /process/entities
POST /process/relationships
POST /process/events
```

## 8.1 Processing Workflow

1. Receive normalized text and processing metadata.
2. Validate the payload.
3. Run the selected processing pipeline.
4. Generate structured candidates.
5. Attach source text spans.
6. Return a JSON response.
7. Return structured errors on failure.

Do not return untraceable assertions without evidence spans.

## 8.2 Entity Extraction

Use spaCy's English entity recognition pipeline.

Implement deterministic mapping from spaCy labels to ARGUS entity types.

Preserve original spans.

Use nullable confidence fields where probabilities are not available.

## 8.3 Relationship Extraction

The MVP should use deterministic candidate-generation rules such as sentence co-occurrence.

Do not pretend that sentence co-occurrence establishes a substantive real-world relationship.

Mark automatically generated edges as `MENTIONED_WITH` until further evidence is reviewed.

## 8.4 Event Extraction

For the MVP, extract date mentions and sentences containing potential events.

Require analyst review before accepting a candidate as an event.

Do not generate exact event dates when the document only provides ambiguous temporal language.

## 8.5 Model Availability

Document how to install required language models.

If a model is unavailable, return a clear service-readiness error rather than silently providing fabricated results.

---

# 9. Background Processing

Document processing should not block the frontend.

Use a database-backed processing-job table and an ASP.NET Core hosted background worker for the local MVP.

### Job States

```text
PENDING
RUNNING
COMPLETED
FAILED
CANCELLED
```

### Processing Sequence

```text
Upload Document
     |
     v
Save Source Metadata
     |
     v
Create Processing Job
     |
     v
Background Worker
     |
     v
Extract Text
     |
     v
Python NLP Processing
     |
     v
Store Proposed Records
     |
     v
Mark Job Completed
     |
     v
Update Analyst Dashboard
```

Persist job state so that jobs are not silently lost during application restarts.

Use timeouts, error logs, and bounded retries.

Avoid duplicated proposed records when reprocessing documents. Implement stable source and mention identifiers or explicit replacement semantics.

The MVP may use polling for processing status. Real-time SignalR updates are optional.

---

# 10. Security, Privacy, and Responsible Use

Although ARGUS is initially designed for local use, it must follow reasonable security practices.

## Requirements

- Validate all API input.
- Restrict document uploads by size and supported type.
- Prevent path traversal.
- Sanitize user-controlled HTML and Markdown rendering.
- Never execute uploaded documents.
- Keep secrets out of source control.
- Use environment variables or local secret storage for configuration.
- Avoid unrestricted server-side fetching of arbitrary URLs.
- Apply request limits and timeouts to external services.
- Log technical failures without exposing sensitive document contents.
- Use established libraries for cryptographic operations.
- Preserve original sources when analytical interpretations change.

### Local-First Security Boundary

The initial application is a single-user local research tool.

It is not production-ready for internet exposure.

Do not expose the API publicly without separately implementing authentication, authorization, HTTPS deployment, rate limits, and additional hardening.

### Responsible Intelligence Analysis

The system must not equate a relationship, location, or name match with evidence of criminal or malicious conduct.

Machine-generated findings must be reviewable.

Analysts must be able to see why a candidate was generated and where the source information originated.

---

# 11. Project Structure

Create a monorepo using the following structure:

```text
argus/
|
+-- README.md
+-- AGENTS.md
+-- .gitignore
+-- .env.example
|
+-- docs/
|   +-- architecture.md
|   +-- api-contracts.md
|   +-- database-schema.md
|   +-- development-plan.md
|
+-- src/
|   |
|   +-- Argus.Web/
|   |   +-- Components/
|   |   +-- Pages/
|   |   +-- Layout/
|   |   +-- Services/
|   |   +-- wwwroot/
|   |
|   +-- Argus.Api/
|   |   +-- Controllers/
|   |   +-- Services/
|   |   +-- BackgroundJobs/
|   |   +-- Middleware/
|   |
|   +-- Argus.Core/
|   |   +-- Entities/
|   |   +-- Enums/
|   |   +-- Interfaces/
|   |   +-- DTOs/
|   |
|   +-- Argus.Infrastructure/
|       +-- Data/
|       +-- Repositories/
|       +-- Storage/
|       +-- ExternalServices/
|
+-- intelligence-service/
|   +-- app/
|   |   +-- main.py
|   |   +-- models/
|   |   +-- pipelines/
|   |   +-- services/
|   +-- tests/
|   +-- requirements.txt
|
+-- tests/
|   +-- Argus.UnitTests/
|   +-- Argus.IntegrationTests/
|
+-- sample-data/
|   +-- documents/
|   +-- investigations/
|
+-- scripts/
```

Use the solution name `Argus.sln`.

Keep business logic out of Blazor components.

Avoid circular dependencies.

Dependency direction should generally follow:

```text
Argus.Web --> API Contracts

Argus.Api --> Argus.Core
Argus.Api --> Argus.Infrastructure

Argus.Infrastructure --> Argus.Core

Intelligence Service --> Independent JSON Contract
```

Use dependency injection and strongly typed configuration.

---

# 12. Development Roadmap

Implementation must be incremental.

Each phase must produce functional, testable software before the next phase begins.

## Phase 1: Foundation and Application Shell

**Objective:** Establish the project architecture and functional investigation management.

Tasks:

1. Initialize the .NET solution.
2. Create the Blazor application.
3. Create the ASP.NET Core API.
4. Create domain and infrastructure projects.
5. Configure Entity Framework Core and SQLite.
6. Implement investigation models.
7. Create and apply the initial migration.
8. Implement investigation CRUD endpoints.
9. Implement a dashboard and investigation management pages.
10. Add input validation and error handling.
11. Create unit and integration tests.
12. Document startup instructions.

**Deliverable:** A working application where users can create, edit, view, and persist investigations.

**Completion Gate:** Investigation data survives application restarts; tests pass; the API and UI launch successfully.

---

## Phase 2: Document Management

**Objective:** Support importing and organizing source information.

Tasks:

1. Implement source records.
2. Add local document storage.
3. Implement text and PDF upload.
4. Add text extraction.
5. Implement SHA-256 hashing.
6. Detect exact duplicates.
7. Build a source library interface.
8. Implement document searching.
9. Display extracted text.
10. Test invalid and malformed uploads.

**Deliverable:** Users can import documents and browse their extracted content.

**Completion Gate:** Imported files persist, exact duplicates are detected, unsupported files fail safely, and document content is searchable.

---

## Phase 3: NLP Integration

**Objective:** Extract and review entities from documents.

Tasks:

1. Initialize FastAPI.
2. Define shared processing contracts.
3. Implement NLP health checks.
4. Install and configure spaCy.
5. Implement entity extraction.
6. Add .NET-to-Python HTTP communication.
7. Implement persistent processing jobs.
8. Store extracted candidates.
9. Build an entity-review interface.
10. Implement approval and rejection.
11. Test model failures and processing retries.

**Deliverable:** Documents generate reviewable entity candidates that can be approved and stored.

**Completion Gate:** Processing completes asynchronously, failures are visible, and all approved entity mentions retain source references.

---

## Phase 4: Entity Graph

**Objective:** Visualize relationships across collected sources.

Tasks:

1. Implement relationship models.
2. Add co-occurrence candidate generation.
3. Create relationship review functionality.
4. Integrate Cytoscape.js.
5. Implement graph APIs.
6. Display approved and proposed edges distinctly.
7. Create graph filtering and searching.
8. Implement graph metrics.
9. Link graph nodes and edges to source evidence.
10. Add graph performance tests.

**Deliverable:** An interactive evidence-linked entity graph.

**Completion Gate:** Users can explore relationships, inspect citations, and manipulate the graph without modifying underlying evidence.

---

## Phase 5: Timeline & Geography

**Objective:** Add spatial and temporal intelligence analysis.

Tasks:

1. Create event and event-evidence models.
2. Implement date candidate extraction.
3. Build event review workflows.
4. Implement chronological timeline.
5. Integrate Leaflet.js.
6. Implement approved geographic locations.
7. Display event markers.
8. Link timeline and map selections to source records.
9. Handle uncertain locations and dates.
10. Add module tests.

**Deliverable:** Interactive chronological and geographic investigation views.

**Completion Gate:** Approved events and locations display accurately, ambiguous dates are not invented, and all displayed facts are traceable.

---

## Phase 6: Assessments and Reports

**Objective:** Support evidence-backed intelligence assessments.

Tasks:

1. Implement claim and evidence models.
2. Create claim editor.
3. Implement supporting and contradictory evidence links.
4. Add confidence-level selection.
5. Display source reliability.
6. Implement assessment review status.
7. Generate Markdown reports.
8. Add references and provenance checks.
9. Implement final report validation.
10. Add automated tests.

**Deliverable:** Users can construct assessments and export source-backed research reports.

**Completion Gate:** Generated reports contain approved information and traceable references, with uncertainty and contradictions represented.

---

## Phase 7: Quality, Performance, and Portfolio Delivery

**Objective:** Prepare a stable and presentable application.

Tasks:

1. Refine UI styling.
2. Add loading and empty states throughout.
3. Implement consistent logging.
4. Optimize common database queries.
5. Add realistic synthetic demonstrations.
6. Implement backup/export functionality.
7. Add end-to-end smoke tests.
8. Document the architecture.
9. Create a setup guide.
10. Add screenshots and an example investigation to the README.

**Deliverable:** A polished portfolio-ready OSINT workstation.

**Completion Gate:** A developer can clone the repository, follow documented setup instructions, run automated tests, and complete the demonstration workflow.

---

# 13. Demonstration Dataset

Create a synthetic investigation titled:

**Operation Meridian — Regional Logistics and Maritime Activity**

This is a fictional training investigation using fabricated organizations and events.

The dataset should contain:

- 3 fictional organizations.
- 6 fictional individuals.
- 5 fictional geographic locations.
- 12 synthetic documents.
- 10 dated events.
- 15 proposed relationships.
- 5 example analytical claims.
- At least 2 contradictory reports.

All names and details must be clearly fictional.

The dataset should demonstrate:

1. Document ingestion.
2. Entity extraction.
3. Entity merging.
4. Co-occurrence analysis.
5. Relationship review.
6. Graph navigation.
7. Timeline reconstruction.
8. Geographic visualization.
9. Contradictory evidence handling.
10. Report generation.

Use this dataset for demonstrations, integration tests, and user acceptance testing.

Do not hardcode its entities into application logic.

---

# 14. Testing Strategy

## C# Unit Tests

Test:

- Investigation validation.
- Entity normalization.
- Entity merge rules.
- Relationship invariants.
- Claim evidence validation.
- Processing state transitions.

## C# Integration Tests

Test:

- Investigation CRUD operations.
- Database persistence.
- Document upload workflow.
- Foreign-key constraints.
- Processing-job persistence.
- Graph API responses.
- Report data retrieval.

Use isolated temporary test databases.

## Python Tests

Test:

- Invalid request handling.
- Entity label mapping.
- Entity span accuracy.
- Empty document handling.
- Duplicate candidate behavior.
- Co-occurrence extraction.
- Date parsing and uncertainty.
- Model availability errors.

## End-to-End Tests

At minimum, verify the following workflow:

1. Create an investigation.
2. Import a synthetic document.
3. Process the document.
4. Review extracted entities.
5. Approve selected entities.
6. View the entity graph.
7. Add an event.
8. Create an evidence-backed claim.
9. Generate a Markdown report.
10. Restart the application and verify persistence.

Tests should be deterministic wherever possible.

Do not rely on live news APIs for automated tests.

---

# 15. Engineering Standards

All generated code must follow the standards below.

### Backend

- Use asynchronous I/O appropriately.
- Use dependency injection.
- Separate domain logic from HTTP controllers.
- Validate request DTOs.
- Use structured logging.
- Return consistent API errors.
- Avoid unnecessary abstractions.
- Avoid placeholder implementations presented as complete.

### Frontend

- Keep components focused.
- Extract reusable UI patterns.
- Centralize API clients.
- Handle loading and error states.
- Use accessible form labels and controls.
- Preserve investigation context when navigating.
- Avoid direct database dependencies.

### Python

- Use type annotations.
- Use Pydantic schemas.
- Separate endpoints from NLP processing.
- Handle empty and malformed inputs.
- Use deterministic algorithms where appropriate.
- Include tests for processing pipelines.

### General

- Document meaningful architectural decisions.
- Maintain a clean Git history.
- Keep configuration outside source files.
- Do not commit databases, uploaded documents, secrets, or generated model artifacts.
- Prefer the simplest implementation satisfying requirements.
- Do not introduce microservices or distributed infrastructure unnecessarily.

---

# 16. Instructions for an Autonomous Coding Agent

These instructions govern how the development agent should execute this project.

## 16.1 Initial Execution

Before implementing features:

1. Inspect the repository.
2. Identify existing project files and conventions.
3. Read `README.md`, `AGENTS.md`, and all relevant architecture documentation.
4. Verify installed SDKs and dependencies.
5. Identify the current development phase.
6. Produce a short implementation plan for that phase.
7. Begin implementation without unnecessarily redesigning completed functionality.

If the repository is empty, initialize the architecture defined in this proposal.

## 16.2 Execution Constraints

**Do not attempt to implement every phase in one development session.**

Work on exactly one phase at a time unless explicitly instructed otherwise.

Do not implement later-phase features as incomplete placeholders.

Do not replace required functionality with mocked services in production code.

Mocks may be used in automated tests.

When requirements are underspecified, choose a simple approach that preserves the architecture and document the decision.

Seek clarification only for decisions that are expensive, irreversible, or materially affect the intended system.

## 16.3 Definition of Done

A development phase is complete only when:

- Required functionality is implemented.
- The application builds successfully.
- Relevant automated tests pass.
- New APIs are documented.
- Relevant migration files exist.
- UI elements perform their advertised actions.
- No critical runtime errors remain.
- Setup instructions are updated.
- The agent has summarized changes and limitations.

Do not declare a phase complete solely because code files were generated.

## 16.4 Validation Requirements

After meaningful changes:

1. Run formatting or lint checks.
2. Build affected .NET projects.
3. Run relevant .NET tests.
4. Run relevant Python tests.
5. Verify API contracts.
6. Perform applicable smoke tests.
7. Report failures precisely.

Never claim a test passed without executing it.

If a tool or dependency is unavailable, describe what was not verified.

## 16.5 Progress Reporting

At the end of each development session, report:

```text
PHASE:
COMPLETED:
FILES CHANGED:
TESTS EXECUTED:
TEST RESULTS:
KNOWN ISSUES:
NEXT STEPS:
```

Keep `docs/development-plan.md` synchronized with actual progress.

## 16.6 Change Management

Before altering established architecture or replacing libraries:

- Identify the reason.
- Evaluate compatibility.
- Preserve existing functionality where possible.
- Update tests.
- Update documentation.

Prefer targeted, reviewable changes over broad rewrites.

---

# 17. MVP Definition

The minimum viable product consists of the following fully working functionality:

1. Create and manage investigations.
2. Upload text and extractable PDF documents.
3. View extracted document content.
4. Run local named entity recognition.
5. Review and approve extracted entities.
6. Generate relationship candidates from document co-occurrence.
7. Display an interactive entity graph.
8. Inspect the source evidence supporting each relationship.

**Phases 1–4 constitute the MVP.**

Timeline visualization, geographic mapping, formal assessments, and reporting are subsequent enhancements.

The MVP must have real persistence, meaningful tests, and an end-to-end demonstration using synthetic data.

---

# 18. Success Criteria

ARGUS will be considered portfolio-ready when it demonstrates:

**Technical Competency**
- Full-stack development.
- REST API design.
- Database architecture.
- Python/C# interoperability.
- Natural language processing.
- Graph algorithms.
- Asynchronous background processing.
- Automated testing.

**Analytical Competency**
- Structured source management.
- Entity resolution.
- Evidence provenance.
- Temporal and geographic organization.
- Contradictory evidence handling.
- Analytical confidence and uncertainty.

**Software Engineering Competency**
- Maintainable architecture.
- Clear separation of concerns.
- Reliable persistence.
- Defensive error handling.
- Useful documentation.
- Reproducible development setup.

The final application should demonstrate how unstructured public-source information can be transformed into a structured, transparent, and navigable analytical workspace without overstating the reliability of automatically extracted findings.

---

# 19. Immediate Implementation Directive

**Begin with Phase 1 only.**

Create the ARGUS solution using the proposed monorepo structure.

Implement:

- Blazor application shell.
- ASP.NET Core API.
- Domain and infrastructure projects.
- SQLite persistence.
- EF Core migrations.
- Investigation CRUD endpoints.
- Dashboard.
- Investigation list, creation, and editing pages.
- Input validation.
- Unit and integration tests.
- Local startup documentation.

Verify the versions and compatibility of all required dependencies before installation.

Do not implement document ingestion, NLP, graphs, timelines, or maps during Phase 1.

Use an organized, dark, professional analytical dashboard aesthetic.

Once Phase 1 is complete, run all available tests and provide a structured completion summary.

**End of Proposal.**