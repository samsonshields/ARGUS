# ARGUS

Local-first OSINT analysis workstation for Windows. Phases 1 and 2 provide investigation
management and a source library: import text/PDF files or pasted text, preserve originals,
search extracted content, edit attribution/reliability and retry text extraction.
Phase 3 has not begun.

## Requirements

- .NET 10 SDK (verified SDK 10.0.102, runtime 10.0.2). SDK pinned in global.json.
- Python 3.12+ for the optional smoke script (verified 3.14.6).
- Internet access for initial NuGet/tool restore. No Docker, Node, paid APIs or Python packages required.

## Local startup

From the repository root in PowerShell:

```powershell
dotnet tool restore
dotnet restore Argus.sln
dotnet build Argus.sln --no-restore
dotnet run --project src/Argus.Api --no-build --launch-profile http
```

In a second terminal:

```powershell
dotnet run --project src/Argus.Web --no-build --launch-profile http
```

Open **http://localhost:5200**. API: **http://localhost:5100**; health: `/health`.
Stop each application with Ctrl+C. Stop existing instances before rebuilding. API startup applies committed migrations before
serving requests. Default database: `src/Argus.Api/data/argus.db` using these commands.
Keep the complete data directory (database and `documents/`) between restarts. No network is required for normal local use.

Set `$env:ARGUS_DATA_DIRECTORY = 'C:/Argus/data'` in the API terminal to override
storage. Set `$env:Api__BaseUrl = 'http://localhost:5100'` in the Web terminal to
override its API address. `.env.example` is reference only; .NET does not auto-load it.
Keep servers bound to loopback: this single-user application has no authentication
and is not ready for public hosting.

Optional explicit migration application (startup already handles this):

```powershell
dotnet ef database update --project src/Argus.Infrastructure --startup-project src/Argus.Api
```

## Validation

```powershell
dotnet test Argus.sln --no-restore
dotnet format Argus.sln --no-restore --verify-no-changes
python scripts/smoke.py
```

The smoke script defaults to a Debug build. It launches temporary loopback API/Web
processes, checks rendered pages/CSS, imports text/PDF/pasted sources, verifies hashes,
duplicates, failures, search, downloads, extraction retry and restart persistence.
It cleans up its processes and temporary database; diagnostics
are in ignored `.local/smoke.log`. It does not drive browser clicks or Blazor circuits.

Manual UI check: create an investigation, edit its scope/status, filter by Archived,
cancel a deletion, restart both apps, reopen the record, and confirm deletion.
Check blank titles and reversed dates produce validation messages.

For verification while Debug instances are running, build/test with `-c Release`,
then set `$env:ARGUS_BUILD_CONFIGURATION = 'Release'` before running the smoke script.
Those checks use separate processes and temporary data. Restart your normal API/Web
instances after building to load the new code and apply the migration.

## Import documents

Open an investigation, select **Source library**, then choose Upload file or Paste text.
Enter the title, optional publisher/URL/publication date and manual source reliability.
URLs are attribution only and are never fetched. Open a source to read escaped extracted
text, download the original, edit metadata or retry extraction. Reliability is an analyst
assessment of the source, not confirmation of any claim.

Try the clearly fictional `.txt` and `.pdf` fixtures in `sample-data/documents/`.
Uploading the same bytes again within one investigation returns a duplicate error with
a link to the existing source. The same document may be used in different investigations.
Investigations containing sources are protected from deletion; archive them instead.

Malformed PDFs, scanned/image-only PDFs, unsupported encodings and extraction-limit
failures retain their original files and show a Failed status with a safe error message.
Retry replaces extracted text only on success; previous successful text survives failures.
There is no OCR. PDF layout/text order can vary; use the original when checking citations.

Default limits (API environment-variable overrides):

| Setting | Default | Environment variable |
| --- | --- | --- |
| File size | 10 MiB | `Documents__MaxFileBytes` |
| Extracted text | 2,000,000 UTF-16 characters | `Documents__MaxExtractedCharacters` |
| PDF pages | 500 | `Documents__MaxPdfPages` |
| Extraction deadline including queue | 20 seconds | `Documents__ExtractionTimeoutSeconds` |

Supported text: strict UTF-8, or BOM-marked UTF-16/32. Originals are byte-preserved;
extracted text uses LF line endings and trims outer whitespace/BOM. PDF extraction uses
PdfPig 0.1.16 in a child mode of the installed API executable; it requires `dotnet` on PATH,
has a 256 MiB managed heap cap, and is terminated on timeout. Files are parsed as data,
never executed. One extraction runs at a time; persistent background/NLP jobs belong to Phase 3.
Keep a stopped-application backup of the complete data directory. A hard crash between
file creation and database commit can leave an unreferenced file; there is no automatic
orphan cleanup. Do not delete preserved documents manually.

Manual Phase 2 UI check: upload both fixtures, reopen after restarting, search for
`logistics`, filter by publication date, try a duplicate and an unsupported file,
paste a source, update reliability, retry extraction, and download the original.

## Structure

- `Argus.sln`: Web, API, Core, Infrastructure, unit and integration test projects.
- `src/Argus.Core`: domain rules, DTOs and repository interface.
- `src/Argus.Infrastructure`: EF Core SQLite persistence, migrations, local document storage and text/PDF extraction.
- `src/Argus.Api`: versioned controllers and application services; owns all writes.
- `src/Argus.Web`: Blazor UI and centralized HTTP client; no database dependency.
- `tests/`: domain unit tests and isolated temporary SQLite/API integration tests.
- `scripts/`: live HTTP smoke verification.

EF Core and EF tool are pinned to 10.0.2. SQLitePCLRaw bundle 3.0.5 replaces the
vulnerable native dependency pulled by EF's default bundle. Native Blazor forms and
custom CSS keep the shell small; MudBlazor compatibility was checked but it is not installed.

Python processing, entities, graphs, timelines and maps are absent. Python service
directories/contracts will be introduced in their authorized phase, without production stubs.

See [specification](docs/project_specifications.md), [architecture](docs/architecture.md),
[API contracts](docs/api-contracts.md), [schema](docs/database-schema.md) and
[development plan](docs/development-plan.md).
