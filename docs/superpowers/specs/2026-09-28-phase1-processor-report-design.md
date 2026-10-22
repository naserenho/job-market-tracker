# Phase 1 — Processor, Gap Analysis and Report

**Status:** Draft for review · **Date:** 2026-09-28 · **Builds on:** [Phase 0 spec](2026-09-27-phase0-raw-archiver-design.md)

## Context

Phase 0 archives raw job-market data twice a week into `data/raw/<run>/`, `data/jobbank/` and `data/inbox/`. The first run on 2026-09-27 held:

| Source | Postings | Notes |
|---|---|---|
| Adzuna | 1,179 unique (1,030 GTA) | Descriptions are about 500-character snippets. 496 GTA postings have a real salary (none predicted) |
| Greenhouse / Lever / Ashby | 5,165 (~980 in Canada) | Full descriptions. Lever and Ashby give structured salary; 433 of 562 Canadian Greenhouse postings state `$` amounts in the text |
| Job Bank | ~54k a month nationally | ~350 software postings a month, so context only |

Phase 1 turns that archive into structured, deduplicated postings in Postgres. It then measures skill demand, compares it with the owner's skill profile, and produces a one-page report. The report can be published as a private claude.ai page. The owner builds the 6-month learning plan and CV emphasis from its gap list.

## Goals

- Rebuild a clean Postgres database from the **entire** raw archive on every run, deterministically and idempotently.
- Interpret postings: scope (GTA or remote-Canada), role family, seniority, salary (CAD), skills, citizenship/clearance flag, and cross-source duplicates.
- Compute skill demand, the owner's gaps and strengths, per-posting match scores, salary statistics, top companies and trends.
- Generate `data/reports/<run>.html` and `data/reports/latest.html`: self-contained, phone-friendly, light and dark.
- Run automatically after the archiver, and fail clearly (never silently) when Docker or Postgres is unavailable.

## Non-goals

- LLM-based skill extraction (phase 3). Phase 1 uses a curated skill list with aliases.
- An interactive dashboard or API (phase 2).
- Automatic publishing. The scheduled task can't publish claude.ai pages, so Claude publishes `latest.html` when the owner asks.
- Changing the phase 0 archiver, apart from the scheduled-task script.

## Approach: rebuild from the archive every run

On every run, `Processor` reads all run folders (never `_dryrun`), `data/jobbank/*.csv.gz` and `data/inbox/*.txt`. It rebuilds the database inside one transaction: it truncates the tables and then inserts everything. Changing a rule, the skill list or the profile therefore takes effect on all history at the next run, with no migrations of data and no backfills. This is the payoff of decision 0001.

Rejected alternatives:
- **Incremental processing:** faster per run, but every rule change would need a backfill mechanism.
- **Processing inside the archiver:** couples collecting with interpreting.

**Volume:** about 7k postings per run and roughly 50 runs in six months. Raw data grows by about 70 MB per run (~3.5 GB by March). The readers accept both `.json` and `.json.gz`, so old runs can be compressed later without code changes.

## Architecture

```
Scheduled task (Tue/Fri 07:00)
  1. Archiver  (phase 0, unchanged)       → data/raw/<run>/…
  2. docker compose up -d postgres        (task script; skipped with a log line if Docker is unavailable)
  3. Processor                            → reads data/** → Postgres → data/reports/<run>.html + latest.html
```

```
job-market-tracker/
  docker-compose.yml                   ← postgres:17, named volume, port 5432 on localhost only
  config/skills.json                   ← skill list (committed)
  config/processor.json                ← GTA cities, remote rules, Job Bank NOC codes, windows (committed)
  config/profile.example.json          ← committed example
  config/profile.json                  ← owner's real profile (gitignored)
  src/Processor/
    Program.cs                         ← CLI, host, exit codes
    Reading/                           ← one reader per raw source → RawPosting
    Interpreting/                      ← scope, role family, seniority, salary, skills, flags, dedupe
    Data/                              ← EF Core DbContext, entities, migrations (Npgsql)
    Rebuild/                           ← orchestrates read → interpret → write in one transaction
    Analysis/                          ← demand, gaps, match, salary stats, companies, trends, Job Bank
    Reporting/                         ← ReportModel → HTML (single self-contained file)
  tests/Processor.Tests/               ← unit tests + Testcontainers integration tests
```

`Processor` does not reference the `Archiver` project. The raw folder layout from the phase 0 spec is the contract between them.

### Configuration and secrets

- The Postgres connection string comes from user-secrets or the env var `ConnectionStrings__Tracker`. `docker-compose.yml` reads its password from `.env`, which is gitignored; the README documents a sample.
- `config/profile.json` is gitignored because the repo may become a public portfolio piece. `config/profile.example.json` shows the format.

## Data model (Postgres, EF Core migrations)

| Table | Columns (key ones) | Notes |
|---|---|---|
| `runs` | `run_id` PK, `started_at` | Taken from each run's `manifest.json` |
| `postings` | `id` PK (`<source>:<source_id>`), `source`, `source_id`, `company`, `title`, `url`, `location_text`, `city`, `is_gta`, `is_remote_canada`, `in_scope`, `role_family`, `seniority`, `salary_min`, `salary_max`, `salary_period`, `salary_origin`, `description`, `is_full_text`, `citizenship_or_clearance`, `first_seen_run`, `last_seen_run`, `duplicate_of` | One row per unique job. The latest sighting's content wins |
| `posting_sightings` | (`posting_id`, `run_id`) PK | Which runs saw each posting |
| `skills` | `id` PK, `name`, `category` | From `config/skills.json` |
| `posting_skills` | (`posting_id`, `skill_id`) PK | |
| `adzuna_aggregates` | (`run_id`, `term`, `kind`) PK, `payload` **jsonb** | Salary histograms and top companies, stored as received |
| `jobbank_monthly` | (`month`, `noc_code`) PK, `noc_name`, `postings`, `vacancies`, `wage_min_median`, `wage_max_median` | Toronto economic region, tech NOC codes only |

## Interpretation rules

All rules are deterministic and unit-tested with real-shaped samples.

**Reading (per source → `RawPosting`)**
- **Adzuna:**
  - Search result files give `id`, `title`, `company.display_name`, `location.display_name` / `area`, `description` (snippet, so `is_full_text=false`), `salary_min/max` and `salary_is_predicted`, and `redirect_url`.
  - Aggregate files go to `adzuna_aggregates`.
- **Greenhouse:** `id`, `title`, `location.name`, `content` (HTML-escaped HTML), `absolute_url`, `company_name`.
- **Lever:** `id`, `text` (the title), `categories.location` / `allLocations`, `workplaceType`, `country`, `descriptionPlain` + `lists` + `additionalPlain`, `salaryRange`, `hostedUrl`. The company is the board slug.
- **Ashby:** `id`, `title`, `location`, `secondaryLocations`, `isRemote`, `workplaceType`, `address.postalAddress`, `descriptionPlain`, `compensation`, `jobUrl`. The company is the board slug.
- **Inbox:** `URL/Company/Title/Location/Saved` header lines, then a blank line, then the body.
  - The ID is `inbox:<file name>` and the posting is always full text.
  - Missing headers produce a warning in the report, not a crash.
- **Job Bank:** a streamed UTF-16 TSV inside gzip. Only rows in the Toronto economic region with a configured NOC code are aggregated.

**Scope**
- **`is_gta`:** the city resolves to one of the GTA municipalities in `config/processor.json`.
- **`is_remote_canada`:** a remote workplace (Ashby `isRemote` / `workplaceType`, Lever `workplaceType`, or "remote" in the title or location) **and** a Canadian location or country.
- **`in_scope`** = `is_gta OR is_remote_canada`. Out-of-scope postings are stored but left out of the statistics.

**Role family** (first match on the title wins): `dotnet` (.NET, C#, ASP.NET) → `fullstack` → `software_engineer` (software engineer/developer, backend, frontend, web developer) → `lead_manager` (tech lead, lead developer, engineering manager, head of engineering) → `other`.
- A lead title that also names .NET stays `dotnet`, and its seniority is `lead`.

**Seniority** (from the title): `junior`, `intermediate`, `senior`, `staff_principal`, `lead`, `manager`, or `unspecified`.

**Salary (CAD only)**
1. Use structured data first: Adzuna `salary_min/max` (not predicted), Lever `salaryRange` with currency CAD, or Ashby compensation components in CAD.
2. Otherwise parse the text for CAD ranges such as `$120,000 – $150,000`, `CA$215K – CA$310K`, `$120K-$150K CAD` or `$55/hour`.
   - Amounts marked US$/USD are ignored.
   - When a posting lists several ranges, the one labelled Canada/CAD/Toronto wins. Plain `$` counts as CAD only for postings in Canada.
3. Hourly amounts become yearly at 2,080 hours. Values outside 30k–500k per year are discarded as parse errors.
4. `salary_origin` records where the salary came from: `structured` / `parsed` / `none`.

**Skills**
- `config/skills.json` holds about 150 skills, each as `{name, category, aliases[]}`.
  - Categories: language, framework, frontend, data, cloud, devops, ai, testing, practice, domain.
- Matching is case-insensitive and whole-word, on the title plus the description with HTML removed.
- Special tokens are handled explicitly:
  - `C#` and `F#`
  - `.NET` / `dotnet` / `ASP.NET`, without matching "asp" in other words
  - `Go`: only "Golang", or "Go" next to a language list separator
  - `R`: only "R programming" or "R," inside a language list
- Aliases can map to several skills, e.g. `AKS` → Azure + Kubernetes.

**Citizenship/clearance flag:** a posting is flagged if it mentions Canadian citizenship or a security clearance (secret, top secret, enhanced reliability). It's flagged, not excluded.

**Cross-source duplicates:** postings with the same normalized company + normalized title + city across different sources form a cluster.
- The preferred member is the one with the full-text description, then the most recent one. The others get `duplicate_of`.
- Statistics count each cluster once.

## Analysis

The window is the postings sighted in the **30 days up to the latest run**, that are in scope, are not duplicates, and belong to the profile's target families.

- **Demand(skill):** the share of full-text postings in the window that mention the skill, overall and per role family.
  - A snippet-based share is shown separately for comparison, never mixed in.
- **Profile levels:** `strong` = 1.0, `working` = 0.6, `learning` = 0.3, `none` = 0. Skills missing from the profile count as `none`.
- **Gap score** = demand × (1 − level). The report shows the top 15, plus "strengths in demand" (demand × level, top 10).
- **Match score(posting):** the share of the posting's tagged skills where the level is `working` or above. The report shows the top 20 open postings (sighted in the latest run) with links.
- **Salary:** by role family × seniority, using yearly midpoints: median, p25–p75 and sample size. Cells with n < 5 are shown as "not enough data".
- **Companies:** the top 20 by distinct in-scope postings in the window.
- **Trends** (shown once the history is ≥ 14 days long):
  - weekly active in-scope postings;
  - the top 10 skills rising or falling, comparing the latest 30 days with the previous 30.
- **Job Bank context:** monthly postings and vacancies for the configured tech NOC codes in the Toronto region.

## Report

`data/reports/<run>.html` plus a copy at `latest.html`. It's a single file with inline CSS and SVG and no scripts. It follows the claude.ai page contract, because it's published as an artifact:
- a 2–4 word `<title>` ("GTA Job Market")
- colours as tokens on `:root`
- dark mode under `@media (prefers-color-scheme: dark)`, guarded by `:root:not([data-theme="light"])` and repeated under `:root[data-theme="dark"]`
- an explicit `body` background
- a 16px side gutter and no horizontal scroll at phone width

The template's visual design is settled during planning with the artifact-design guidance.

Sections:
1. Snapshot
2. Top skills in demand
3. Your gaps / strengths in demand
4. Salary
5. Who's hiring
6. Best-matching open postings
7. Trends
8. Job Bank context
9. Methodology, data warnings (e.g. inbox files with missing headers) and "Jobs data partly by Adzuna"

## CLI and exit codes

- `Processor` rebuilds the database and writes the report.
- `Processor --report-only` regenerates the report from the current database.

| Code | Meaning |
|---|---|
| 0 | OK |
| 1 | Processing failed (the report isn't written; the previous `latest.html` is kept) |
| 2 | Config or profile invalid |
| 3 | Database unavailable (Docker or Postgres not running) |

## Scheduling

`scripts/register-task.ps1` changes so that the task runs three steps, all appending to `data/archiver.log`:
1. `Archiver`
2. `docker compose up -d postgres`, which is skipped with a logged message if `docker` isn't available
3. `Processor`

Processor runs even if the archiver failed. The README recommends setting Docker Desktop to start at login.

## Testing

**Unit tests** (no Docker) cover:
- each reader against trimmed real-shaped fixtures;
- scope, role family, seniority, salary parsing (including US+CA dual ranges, K suffix, hourly and the out-of-range guard), skill matching edge cases (`C#`, `.NET`, `Go`, `R`, multi-skill aliases), citizenship flag, inbox parsing and dedupe clustering;
- gap, match and salary-statistics maths;
- report sections rendered from a hand-built `ReportModel`.

**Integration tests** (Testcontainers PostgreSQL, trait `Integration`) cover:
- migrations apply;
- a rebuild from a 3-run fixture archive gives the expected postings, sightings and skills;
- running the rebuild twice gives identical results (idempotent);
- `--report-only` renders from the database.

**Live check:** run it on the real archive and check the numbers for sanity:
- in-scope postings of the same order as the data profile;
- top skills that include C#, .NET, React, SQL, Azure/AWS;
- no in-scope salary medians outside 50k–300k.

## Decision notes to add

- **0004:** Postgres for the tracker database, with SQL Server as the rejected alternative.
- **0005:** a full rebuild from the archive instead of incremental processing.
- **0006:** a rule-based skill list before an LLM, which is cheap, deterministic and testable; the LLM comes in phase 3.

## Open items (resolved during planning or build)

- The exact GTA municipality list and the tech NOC code list (planning).
- The initial content of `skills.json` (~150 entries) and the owner's draft `profile.json` (planning; the owner reviews the profile).
- Whether EF Core `SaveChanges` is fast enough for about 100k `posting_skills` rows, or whether Npgsql binary COPY is needed. Measured in the build.
