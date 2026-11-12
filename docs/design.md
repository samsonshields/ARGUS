# Interface direction

ARGUS is a local evidence workstation for open-source research. The interface follows a
dark "command console" language: near-black ground, large inset rounded bands, a horizontal
top navigation, big medium-weight headlines with tight tracking, bordered stat cards and
corner-bracket frames around the graph and map. It keeps the conventions analysts already
read fluently: a data-handling marking strip, Zulu time, ICD 203 confidence wording and
Admiralty-style source grades.

## Principle: color is evidence state

Neutral greys and white carry the layout. Color appears only where it says something about
the evidence, and the same language is used in the graph, timeline, record lists and tags:

| Meaning | Treatment | Token |
| --- | --- | --- |
| Analyst reviewed / approved | Green, solid dot | `--reviewed` #1ed760 |
| Machine proposed, awaiting analyst review | Amber, hollow dot, dashed edges | `--proposed` #f6b704 |
| Contradiction, failure, destructive action | Red | `--alert` #ff5b48 |
| Events on the map, location nodes in the graph | Blue | `--info` #64a7ff |
| Rejected, archived, cancelled, neutral | Grey | `--ink-3` #8f8f94 |

Base palette: ground #0f0f0f, band #161618, raised #1f1f22, inset #0b0b0c, rule #2a2a2d,
control border #6b6b70, ink #fafafa, secondary ink #b4b4b4, tertiary ink #8f8f94. Text pairs
meet WCAG AA and form-control borders meet 3:1. Status is never conveyed by color alone: every
pill carries its status word and proposed items use a hollow marker and dashed rules.

## Type

IBM Plex Sans (headlines weight 500 with -0.03em tracking, body 15px) and Source Code Pro
for hashes, offsets, coordinates and UTC timestamps. Both are vendored as variable woff2
files under `wwwroot/vendor` (SIL OFL 1.1, checksums in `integrity.json`), so the
workstation still makes no network requests. Status pills and table headers use small
tracked capitals; everything else is sentence case.

## Components

* **Primary action:** solid white with black text. **Secondary:** 1px white outline.
  Dense in-list actions (pagers, citation and job buttons) use a quieter grey outline.
* **Section marker:** ring-and-dot before each section heading.
* **Stat cards:** the dashboard evidence pipeline and queue use bordered cards with large
  numerals; the overview "analytic workflow" is a grid of cards. Numbers appear only where
  the content is a real sequence.
* **Bands:** a larger section is an inset `#161618` rounded band on the ground.
* **Frames:** the graph and map canvases carry corner brackets and a faint dot or coordinate grid.
* **Hero:** the dashboard opens with a centered headline over a faint masked grid.

## Layout

```text
[ marking strip (configurable, Workstation:Marking) ]
Brand   Dashboard  Investigations  Processing jobs  Workspace backup      Local workspace  UTC
Investigations / Case title [status]
Overview  Sources  Entities  ...  Reports                      (section tabs)
Page title                                          Secondary  Primary
Lede
Filter toolbar
Record list                      | Inspector (sticky; stacks below 1280px)
Footer: brand, note, links
[ marking strip ]
```

All page content is left aligned except the dashboard hero and stat cards. Empty, loading and
error states are distinct and give a concrete next action. Focus is a 2px white outline; reduced
motion and forced colors are respected; there is no page-load animation.

The marking text comes from `Workstation:Marking` in the Web app settings; an empty value hides
it. The default describes the local workspace content (open-source research) and is not a
classification decision about imported material.

## Alignment correction (2026-11-12)

Keep the existing palette, typefaces, navigation and panel treatments. Side-by-side
review columns share one top edge: the first list heading/record and first inspector
or empty state start at the grid's top, with section spacing retained between later
blocks. Apply this shared rule to claims, chronology, locations and relationships.
Source import keeps the browser's inline filename and removes the duplicate caption;
file selection must not change the alignment of the adjacent input-method field.
Form fields keep their contents at the top of each grid cell so a taller native file
control or validation message does not stretch adjacent labels and move their controls.
Check empty and selected inspectors, other two-column views, and stacked mobile layouts
in the actual browser before considering additional spacing changes.

Claims follow-up: place the Claims heading above the review row, keeping its count
over the left column. Align the claim list/empty-state box with the inspector box,
not the heading with the inspector border. Keep the heading full width when stacked.
