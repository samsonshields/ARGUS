# Local setup and verification

ARGUS is a single-user local workstation. The verified target is Windows x64 with
.NET SDK 10.0.102/runtime 10.0.2, Python 3.14.6 and installed Microsoft Edge.
Python 3.12+ is the project minimum; the complete lockfile reproduces the tested
Windows/Python 3.14 environment. Other operating systems have not been verified.

## Get a source checkout

Clone this repository or unpack its source archive, then open PowerShell in its root.
The root must contain Argus.sln, global.json, Directory.Build.props, dotnet-tools.json,
src, tests, intelligence-service, scripts, docs and sample-data. In particular,
`src/Argus.Infrastructure/Data` contains C# source/migrations and must be included.
Only actual runtime data directories and database files are ignored.

```powershell
dotnet --version
python --version
dotnet tool restore
dotnet restore Argus.sln
python -m venv intelligence-service/.venv
intelligence-service/.venv/Scripts/python.exe -m pip install -r intelligence-service/requirements-lock.txt
intelligence-service/.venv/Scripts/python.exe -m pip check
dotnet build Argus.sln -c Release --no-restore
```

The lockfile includes the trained English model's pinned URL and all test packages.
If you use requirements-dev.txt instead, separately install the trained model with
`python -m spacy download en_core_web_sm` using the venv interpreter. Installation
requires internet; runtime NLP/graph/coordinate-grid use existing local assets offline.
No Docker, paid service, hosted LLM or Node runtime is required to run ARGUS.

## Start three local services

Use three terminals at the repository root. Do not run a second API against the same
data directory. Stop an existing process before rebuilding its binaries.

Terminal 1, local NLP:

```powershell
intelligence-service/.venv/Scripts/python.exe -m uvicorn app.main:app --app-dir intelligence-service --host 127.0.0.1 --port 8000
```

Terminal 2, API and data:

```powershell
$env:ARGUS_DATA_DIRECTORY = "$PWD/.local/workspace"
dotnet run --project src/Argus.Api -c Release --no-build --launch-profile http
```

Terminal 3, browser application:

```powershell
dotnet run --project src/Argus.Web -c Release --no-build --launch-profile http
```

Open http://localhost:5200. API http://localhost:5100/health; NLP readiness
http://localhost:8000/health. API OpenAPI is `/openapi/v1.json`; Python documentation
is `/docs`. API startup applies committed migrations; no manual schema initialization
is needed. Claims/reporting from existing sources work even if NLP is unavailable.

The example uses ignored `.local/workspace` so user evidence cannot be staged accidentally.
Without an override, `dotnet run` stores data under `src/Argus.Api/data`. Keep database
and originals together. Environment examples are in `.env.example`; .NET does not load
that file automatically. Keep all services bound to loopback; there is no authentication.

## Run verification

With the venv installed and normal processes stopped or using a different build:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify.ps1 -Full
```

The first command restores/builds/tests .NET, verifies formatting, and runs Python
tests/lint/format/dependency checks. Full additionally starts isolated temporary API,
Web and NLP instances and runs six live smoke workflows. It uses installed Edge;
`ARGUS_BROWSER_CHANNEL=chrome` selects installed Chrome where supported. No browser
download is required. The Meridian smoke regenerates fictional portfolio screenshots.
Temporary services stop in `finally` blocks; logs and transient outputs are in `.local`.

To independently validate migration consistency and NuGet advisories:

```powershell
dotnet ef migrations has-pending-model-changes --project src/Argus.Infrastructure --startup-project src/Argus.Api --configuration Release --no-build
dotnet list Argus.sln package --vulnerable --include-transitive
```

The repository's source-only checkout check respects Git ignore rules and builds
without existing bin/obj output. The optional flag also installs a fresh Python venv:

```powershell
intelligence-service/.venv/Scripts/python.exe scripts/verify_clean_checkout.py --fresh-python
```

This copies source from the current tracked/unignored working tree, not a remote Git
commit. It leaves its reviewable copy/log under ignored `.local/clean-source-*` and
`.local/clean-checkout.log`. It never copies credentials, databases or local uploads.

## Complete the demonstration

Follow [the Operation Meridian walkthrough](demo-walkthrough.md). It loads twelve
fabricated documents through real import/processing/review APIs, then demonstrates
entity merge, relationships, timelines, maps, contradictions and report generation.
It does not write to SQLite directly or install fixture data during normal startup.

## Troubleshooting

| Symptom | Action |
| --- | --- |
| SDK not found/version mismatch | Install the SDK pinned by global.json; check `dotnet --list-sdks`. |
| NLP readiness 503 | Use the venv interpreter; install the locked English model, then restart NLP. No fallback NER is fabricated. |
| Jobs remain pending | Ensure one API worker is running with `Intelligence__WorkerEnabled=true`; inspect Processing jobs. |
| Text exceeds pipeline limit | Split research into smaller source documents; processing never truncates evidence silently. |
| UI cannot reach API | Check the API health URL and `Api__BaseUrl`, then use Reload this view. |
| Build has locked DLLs | Stop processes using that configuration; use Release for isolated verification while Debug is open. |
| Backup or report validation fails | Inspect the source's original/hash and storage availability. Do not replace missing evidence with invented text. |
| Restore destination already exists | Choose a new directory; restore deliberately never replaces an existing workspace. |
| Online map tiles fail | Continue using the offline coordinate grid and reviewed markers; online imagery is optional. |

Application logs are structured JSON. API requests include trace ID, route template,
status and duration; submitted documents, query text and parser exception details are
not logged by ARGUS. See [quality and recovery contracts](quality-contracts.md) for
limits, privacy and recovery semantics.
