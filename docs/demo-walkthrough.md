# Operation Meridian demonstration

**Operation Meridian — Regional Logistics and Maritime Activity** is a fictional
training investigation. Every named person, organization, place and activity is
fabricated; hypothetical map points lie in open water and identify no real facility.
This exercise must not be presented as an assessment of actual people or events.

## Load the exercise

Start API, Web and the trained local NLP service using [setup.md](setup.md), then run:

```powershell
intelligence-service/.venv/Scripts/python.exe scripts/load_demo.py --api-url http://localhost:5100
```

The loader creates a **new investigation each run** and writes its record IDs to
`.local/meridian-result.json`. It never deletes an existing workspace. It uses only
loopback HTTP APIs, real source imports, real NLP proposals and explicit training
review decisions. Allow the worker to finish; an unavailable model/worker fails with
an actionable message rather than substituting fake results. A partial run retains
the investigation and its checkpoint so it can be inspected or archived.

The choices in sample-data/operation-meridian/manifest.json are fixture-specific analyst
decisions, not automatic production review rules. All names and choices live in data,
not application logic. Rejected/unselected date/other mentions remain proposed for review.

| Material | Completed exercise |
| --- | --- |
| Organizations | Aster Maritime Union, Meridian Port Council, Northstar Transit Bureau |
| People | Mara Ellison, Jonas Vale, Leila Corven, Tomas Arlen, Nadia Selwyn, Ivo Maren |
| Places | Port Meridian, Aster Quay, Selwyn Anchorage, Northstar Depot, Corven Point |
| Documents | 12 fabricated text records, including 2 conflicting accounts |
| Canonical entities | 14; original alias entity retained in merge history |
| Relationships | 18 generated; 15 proposed, 3 analyst-reviewed |
| Timeline | 10 approved dated events: 8 exact, 1 month-only, 1 year-only |
| Geography | 5 approved approximate locations; 4 source-supported event markers |
| Assessments | 5 approved claims; 2 explicitly unresolved contradictions |
| Reports | 1 finalized Markdown snapshot with complete source references |

## Walk through the investigation

1. Open the new investigation from the dashboard. Its description and every original
   document explicitly identify the exercise as fictional.
2. In Sources, open record 01 and download its original. Inspect the immutable hash,
   extracted text and completed entity-processing job. The trained model misclassifies
   some invented names; the recorded review corrects their canonical types.
3. In Entities, open Mara Ellison. The alternate `Mara E. Ellison` entity was explicitly
   merged, retaining the original mention strings, citations and alias history. The
   source/target IDs are included in the loader's result JSON.
4. Open Relationships and Graph. Compare fifteen proposed edges with three reviewed
   ones. The Mara/Aster edge is MEMBER_OF only because its cited fictional sentence
   explicitly states membership. Other reviewed edges remain low-assertion co-mentions.
   Select nodes/edges, expand neighbors, filter and highlight a structural path.
5. Open Timeline. Ten events sort chronologically while month/year labels retain
   uncertainty. The two conflicting secondary dates remain proposals, not silently
   reconciled events. Select a row to inspect the preserved sentence and source.
6. Open Geography. Amber circles show selected uncertainty radii. Only four events
   have a source-supported place association; the other six are not assigned invented
   map positions. The coordinate grid works offline. Online OSM tiles are optional.
7. Open Assessments and enable Unresolved contradictions only. Compare records 01/11
   and 02/12. Read the SUPPORTS/CONTRADICTS/CONTEXT labels, manual source grades and
   analyst reasoning. Approval explicitly acknowledges, and does not resolve, conflicts.
8. Open Reports, inspect the saved snapshot, and download Markdown. Check confidence,
   uncertainty, conflicting information, limitations, P-number reference anchors, exact
   quotations, snapshot hashes and original hashes. An insufficient-information claim
   remains explicitly labeled. Edits require a new review/report, not a rewrite of history.
9. Open Workspace backup and download the archive. Follow the recovery steps below to
   restore into a new directory, restart API/Web, and reopen the same report and originals.

## Recovery rehearsal

Stop the API before changing the data directory it uses. Restore itself only writes to
a new destination and never changes the old workspace:

```powershell
dotnet src/Argus.Api/bin/Release/net10.0/Argus.Api.dll --restore-backup "C:/Backups/argus-backup.zip" "C:/Argus/meridian-restored"
$env:ARGUS_DATA_DIRECTORY = 'C:/Argus/meridian-restored'
dotnet run --project src/Argus.Api -c Release --no-build --launch-profile http
```

Restart/reload Web, then open the investigation using its original GUID. Check the
report hash, exact citations, source bytes, review decisions and saved map focus.
Do not point two API instances at the same directory.

## Automated acceptance

```powershell
$env:ARGUS_BUILD_CONFIGURATION = 'Release'
intelligence-service/.venv/Scripts/python.exe scripts/smoke_demo.py
```

This starts three real services on temporary ports/data, runs the entire fixture,
drives Edge navigation/filtering and a real backup download, checks mobile overflow,
restores with the real CLI, restarts and verifies report/evidence/hash persistence.
It blocks external browser requests and regenerates docs/screenshots/meridian-*.png
using fictional data only. Other smoke scripts cover detailed graph/review/form behavior.
