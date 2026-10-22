# Phase 0 — Raw Snapshot Archiver

**Status:** Draft for review · **Date:** 2026-09-27

## Context

This repo is a personal job-market tracker for the Toronto/GTA software market, built ahead of a job search. The tracker answers three questions: which skills GTA employers ask for, what they pay, and where the owner's skill gaps are.

The project runs in phases. Each one delivers usable output and uses a technology the owner's target roles ask for:

| Phase | Delivers |
|---|---|
| **0 (this spec)** | A raw archive of job-market snapshots |
| 1 | .NET ingestion worker: dedupe, normalize, skill tagging, Postgres in Docker, CLI report |
| 2 | API, dashboard, gap analysis, snapshot export to a claude.ai artifact (.NET Aspire, OpenTelemetry) |
| 3 | LLM skill extraction and a paste inbox (Azure OpenAI via Microsoft.Extensions.AI) |
| 4 | Azure deployment (Container Apps, Functions timer, Bicep/Terraform, GitHub Actions, Key Vault) |

Phase 0 comes first for one reason: **trend data only exists if snapshots are collected from now on.** A missed week of history can't be recovered later. Phase 0 therefore collects raw data and does no parsing. Phase 1 will re-process the entire archive.

## Goals

- Twice a week, save the raw responses from every legitimate source to dated folders on disk.
- Never lose a run silently. Every run writes a manifest recording what succeeded and what failed.
- Stay well within Adzuna's free-tier limits.
- Keep secrets out of the code and out of git.

## Non-goals

- Parsing, deduplication, skill tagging and any database (all phase 1).
- A dashboard (phase 2).
- Scraping LinkedIn, Indeed, Glassdoor or Workday career sites. Postings from those sites come in through the manual inbox.

## Sources

### 1. Adzuna API (country `ca`)

- **Search terms:** `.net developer`, `c# developer`, `full stack developer`, `senior software engineer`, `software engineer`, `tech lead`, `engineering manager`.
- **Scopes:** each term runs twice:
  - `gta`: `where=Toronto`, `distance=50` (km), which covers the GTA.
  - `remote`: the term plus `remote`, searched across Canada. Phase 1 decides properly which results are really remote.
- **Paging:** `results_per_page=50`. A query stops when a page returns fewer than 50 results, or after **4 pages**.
- **Aggregate endpoints (GTA, per term):** `histogram` (salary distribution) and `top_companies`.
- **Budget:** at most **80 calls per run**, enforced in code. The query plan is ordered so the aggregate endpoints are never the calls that get skipped. At 2 runs a week that's about 700 calls a month, against the free tier's 250/day and 2,500/month.
- **Terms of use:** personal research is permitted. Any displayed output must credit Adzuna as the source.

### 2. Company job boards (full descriptions)

A config list of GTA and remote-Canada tech employers, each identified by `{ats, slug}`:

- Greenhouse: `GET https://boards-api.greenhouse.io/v1/boards/{slug}/jobs?content=true`
- Lever: `GET https://api.lever.co/v0/postings/{slug}?mode=json`
- Ashby: `GET https://api.ashbyhq.com/posting-api/job-board/{slug}?includeCompensation=true`

Each board is downloaded in full, with no filtering by location or role (that happens in phase 1). **Every entry is checked to return HTTP 200 before it goes into the config.** The initial list has 39 companies, each confirmed on 2026-09-27 to have at least 3 postings in Canada.

### 3. Job Bank open data (monthly)

Government of Canada dataset `ea639e28-c0fc-48bf-b5dd-b8899bd43072` on open.canada.ca: a monthly CSV of Job Bank postings, including NOC codes, location and wages.

- Each run asks the CKAN API (`package_show`) for the dataset's resources and keeps the **newest 12 months of English CSVs** on disk. Any of those months that's missing gets downloaded, so the first run backfills 12 months and later runs fetch only new months.
- The files are UTF-16 tab-separated, about 48 MB a month. They're stored gzip-compressed (the bytes inside the gzip are unchanged).

### 4. Manual inbox

`data/inbox/` holds `.txt` files the owner pastes postings into, typically from LinkedIn or Indeed.

- Phase 0 only creates the folder, adds a README explaining the expected format, and records the file count in the manifest.
- Phase 1 processes the files.

## Storage layout (all under `data/`, which is gitignored)

```
data/
  raw/
    2026-09-29_0700/                  ← one folder per run (local time, yyyy-MM-dd_HHmm)
      adzuna/search/<term-slug>-<gta|remote>-p<N>.json
      adzuna/histogram/<term-slug>-gta.json
      adzuna/top_companies/<term-slug>-gta.json
      boards/<ats>-<slug>.json
      manifest.json
  jobbank/
    <yyyy-MM>_<resource-id>.csv.gz    ← monthly; outside run folders, never re-downloaded
  inbox/
    README.md
    *.txt
```

- Responses are saved **byte-for-byte as received**, with no reformatting.
- Adzuna credentials never appear in file names, the manifest or the repo. The one exception: Adzuna's own `redirect_url` values inside saved response bodies contain the app_id. That's Adzuna's data, and `data/` is gitignored.
- `manifest.json` records:
  - `runId`, start and end times, tool version
  - one entry per request: source, URL (with `app_id`/`app_key` removed), HTTP status, byte count, duration, output file, error message
  - totals per source, and the Adzuna call count against the budget
  - the inbox file count

## Architecture

A .NET 10 console app built on the Generic Host, so it can grow into the phase 1 ingestion worker instead of being thrown away.

```
job-market-tracker/
  JobMarketTracker.slnx
  src/Archiver/
    Program.cs                 ← host setup, config, DI, exit code
    Configuration/             ← options classes bound from config/sources.json
    Sources/
      IArchiveSource.cs        ← one interface; each source plans and fetches its own requests
      AdzunaSource.cs
      JobBoardSource.cs        ← Greenhouse / Lever / Ashby
      JobBankSource.cs
    Storage/RunFolder.cs       ← path building, slugging, atomic writes
    Manifest/ManifestWriter.cs
  tests/Archiver.Tests/        ← xUnit
  config/sources.json          ← search terms, scopes, page caps, budget, board list
  scripts/register-task.ps1    ← creates/updates the Windows scheduled task
  docs/decisions/              ← short "why" notes (ADRs)
  README.md
```

- **HTTP:** `IHttpClientFactory` with `Microsoft.Extensions.Http.Resilience`. The standard resilience handler retries 429 and 5xx responses with exponential backoff and jitter, and honours `Retry-After`. Each request times out after 30 s.
- **Politeness:**
  - Adzuna requests run one at a time, with at least 3 s between them (well under the 25/minute limit).
  - Board requests run at most 4 at a time.
- **Isolation:** each source runs independently. An exception in one source is caught, recorded in the manifest, and doesn't stop the other sources.
- **Atomic writes:** each response is written to `*.tmp` and then renamed, so a crash never leaves a half-written file that looks complete.

## Configuration and secrets

- `config/sources.json` (committed) holds the terms, scopes, caps, budget and board list.
- Adzuna `AppId` and `AppKey` come from **.NET user-secrets**. These are loaded explicitly in every environment, so the scheduled task works. Environment variables `ADZUNA__APPID` and `ADZUNA__APPKEY` override them.
- If the Adzuna credentials are missing, the Adzuna source is skipped and the skip is recorded in the manifest. The other sources still run.

## Scheduling

`scripts/register-task.ps1` registers a Windows Task Scheduler task:
- **When:** Tuesday and Friday at 07:00, running as the current user.
- **Command:** `dotnet run --project src/Archiver -c Release`, or the published executable. The command is chosen when the task is built.
- **Missed runs:** `StartWhenAvailable` is enabled, so a run missed while the PC was off happens at the next logon.
- **Log:** console output is appended to `data/archiver.log`, so a crash before the manifest is written is still visible.
- **Re-running:** the script is idempotent, so running it again updates the task.

## Exit codes

| Code | Meaning |
|---|---|
| 0 | Every source succeeded, or partial success (details in the manifest) |
| 1 | Every attempted source failed, e.g. no network |
| 2 | Configuration invalid, e.g. `sources.json` unreadable |

## CLI

- `Archiver` performs a full run.
- `Archiver --dry-run` makes one request per source into `data/raw/_dryrun/`. It's used to check credentials and board slugs.
- `Archiver --verify-boards` checks every configured board returns 200 and prints the result.

## Testing

**Unit tests (xUnit, no network; HTTP is faked with a stub `HttpMessageHandler`):**
- The query plan: terms × scopes × pages, the 80-call budget cap, and the ordering that keeps the aggregate endpoints under budget.
- Pagination stops on a short page and at the page cap.
- Slugging and path building (`.net developer` → `net-developer`), including run-folder naming.
- Credential redaction: URLs in the manifest never contain `app_id` or `app_key`.
- Job Bank: only resources not already on disk are chosen, and the first run backfills 12 months.
- Exit code logic across the all-success, partial and all-failed cases.

**Live check:** `--dry-run` once the Adzuna credentials are set.

## Decision notes (docs/decisions/)

1. **0001: store raw, parse later.** The parsing rules will change in phase 1. With raw data, the full history can be re-processed at any time.
2. **0002: official APIs only, no scraping.** Covers ToS and legal risk, stability, and why the inbox covers LinkedIn and Indeed.
3. **0003: .NET console app instead of a quick script.** It grows into the phase 1 worker, stays in the target stack, and brings DI and resilience from the start.

## Open items (resolved during build)

- The final board list. Every slug is checked with `--verify-boards` before it's committed. **Resolved 2026-09-27:** all 39 boards pass.
- Adzuna parameter names for the aggregate endpoints are confirmed against the live API. **Resolved 2026-09-28** by direct calls, because `--dry-run` only exercises search:
  - `histogram` and `top_companies` honour `where=Toronto`: 706 postings against 2,492 with no location, and identical to the `location0/1/2` form.
  - Both endpoints **reject `distance` with HTTP 400**, so for the aggregates the `gta` scope means Adzuna's own Toronto area, not a 50 km radius. Search keeps `distance=50`.
- Adzuna retries are disabled so every wire call counts against the budget. A transient 503, seen once on 2026-09-28, ends that query for that run only.
