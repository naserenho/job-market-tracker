# Phase 2a — Interactive Dashboard (Skill and Salary Explorer)

**Status:** Draft for review · **Date:** 2026-09-28 · **Builds on:** [Phase 1 spec](2026-09-28-phase1-processor-report-design.md)

## Context

Phase 1 rebuilds a Postgres database from the raw archive twice a week and writes a static one-page report. The owner wants to explore that data interactively: filter it, drill into skills, compare salaries and browse postings. The owner prioritised four features. Phase 2 is split into three sub-projects, each with its own spec:

| Sub-project | Delivers |
|---|---|
| **2a (this spec)** | Read-only dashboard: overview, skill explorer, salary explorer, postings browser |
| 2b | Shortlist and application tracker. Its tables live outside the rebuild's TRUNCATE, keyed by stable posting ids |
| 2c | "Tailor my CV for this posting" view |

**Decisions already made with the owner:**
- The dashboard runs on this PC only. The static report page stays the phone view.
- The front end is React + TypeScript (Vite).
- The owner studies the code rather than writing it, so each decision gets a note in `docs/decisions/`.

## Goals

- A local web dashboard that always shows the current database. Nothing to rebuild or republish after a collection.
- One source of truth for the numbers. The API reuses phase 1's tested analysis code, so the dashboard and the report never disagree.
- Learning value against the owner's gap list:
  - ASP.NET Core minimal APIs
  - **.NET Aspire and OpenTelemetry** (observability is gap #10)
  - React + TypeScript, TanStack Query and Vitest

## Non-goals

- Writing data: the shortlist and applications are 2b.
- Access from other devices, authentication, or cloud hosting (phase 4).
- Changing what phase 1 computes. The only change is extending the shared model where a filter needs it (see the refactor section).

## Architecture

```
src/Tracker.Core/      class library: everything from src/Processor except Program.cs
                       (Configuration, Reading, Interpreting, Data + Migrations, Rebuild, Analysis, Reporting, Stats, ErrorText)
src/Processor/         console app: Program.cs only, references Tracker.Core (behaviour unchanged)
src/Api/               ASP.NET Core minimal API, references Tracker.Core and ServiceDefaults
src/Dashboard/         React + TypeScript (Vite), TanStack Query, React Router, Recharts
src/AppHost/           .NET Aspire 13 AppHost: Api + Dashboard, wired to the existing Postgres
src/ServiceDefaults/   Aspire service defaults: OpenTelemetry, health checks, resilience
tests/Tracker.Core.Tests/  renamed from Processor.Tests (same tests)
tests/Api.Tests/       unit tests for the query functions, plus integration tests (WebApplicationFactory + Testcontainers)
```

### Refactor: `Tracker.Core`

- Mechanical only. Files move from `src/Processor` to `src/Tracker.Core`, and namespaces change from `Processor.*` to `Tracker.*`. `Program.cs` stays in `src/Processor` and references the library.
- The EF migrations move too. The migration id (`20260928093654_InitialCreate`) doesn't change, so the existing database needs no new migration.
- Processor's user-secrets id stays on the `Processor` project.
- **Gate:** all existing tests (101 archiver + 238 processor) pass after the move, and a live `Processor --report-only` still writes the same report, before any new feature code is written.
- `AnalysisPosting` gains `IsGta`, `IsRemoteCanada` and `City`, so the API can filter by location. `AnalysisLoader` fills them. The existing analyzer tests keep passing.

### Running it

- `dotnet run --project src/AppHost` starts the Aspire dashboard, the API and the Vite dev server. `scripts/dashboard.cmd` does the same, for double-clicking.
- The AppHost declares the database with `AddConnectionString("tracker")`, read from the AppHost's user-secrets `ConnectionStrings:tracker`. It reaches the **existing** Compose Postgres on port 5434 and doesn't start a new one. The API receives the connection string through Aspire.
- The Dashboard runs through `AddViteApp`. Vite proxies `/api` to the API's endpoint, which Aspire injects as an environment variable.
- The scheduled `Processor` is unchanged. It keeps rebuilding the database, and the API only reads it.

## API (read-only JSON)

### Common filters (query string)

| Parameter | Values | Default |
|---|---|---|
| `families` | comma list of `dotnet`, `fullstack`, `software_engineer`, `lead_manager`, `other` | the profile's target families |
| `seniority` | comma list of `Junior`, `Intermediate`, `Senior`, `StaffPrincipal`, `Lead`, `Manager`, `Unspecified` | all |
| `where` | `gta`, `remote`, `all` | `all` |
| `window` | `30`, `60`, `90`, `all` (days up to the latest run) | `30` |

Unknown values return **400** with a problem-details body that names the parameter and the allowed values.

### Endpoints

| Endpoint | Extra parameters | Returns |
|---|---|---|
| `GET /api/meta` | — | Latest run (id, time), number of runs, history days, filter options (families, seniorities, skill categories, top 50 companies), the profile's target families |
| `GET /api/overview` | filters | The same content as the report's `ReportModel` (snapshot, top skills, gaps, strengths, salary cells, companies, matches, trends, warnings), computed by `Analyzer` over the filtered postings |
| `GET /api/skills` | filters, `text` (`full` default / `snippet` / `all`), `category` | Every skill with share, count and the owner's level. When the window has an earlier comparison window of the same length: previous share and change |
| `GET /api/skills/{name}` | filters, `text` | That skill's share and count; up to 50 postings mentioning it (newest first); median salary with vs without the skill (n for each); the top 10 skills that appear alongside it; the top 10 companies asking for it. 404 for an unknown skill |
| `GET /api/salaries` | filters, `skill`, `company` | Salary cells by role × seniority (n, median, p25, p75, using phase 1's n ≥ 5 rule), a histogram of yearly midpoints in 20k buckets, and up to 100 salaried postings |
| `GET /api/postings` | filters, `q` (title/company contains, case-insensitive), `skill`, `company`, `sort` (`newest` / `match` / `salary`), `page`, `pageSize` (≤ 100, default 25) | Paged postings: title, company, location, salary, match score, first seen, skills, url, citizenship flag. Includes the total count |

Match scores, levels and gaps use the owner's `config/profile.json`, read at API start-up.

### `MarketData` service

- Loads `(postings, runs, jobBank)` with `AnalysisLoader` and keeps it in memory. That's about 2k postings, a few MB.
- At most once every 60 s, a request triggers a cheap check of the latest run id and the run count. If either changed, the data is reloaded; a lock ensures only one reload runs at a time.
- The query functions (`SkillQueries`, `SalaryQueries`, `PostingQueries`, `Filters`) are pure functions over the in-memory data. They get unit tests without any database.

## Errors

- **Postgres unreachable** (at load or reload): **503** problem details with the message "The tracker database isn't running. Start Docker Desktop (and `docker compose up -d postgres`)". If data was loaded earlier, the last good copy is served instead, with a response header `X-Data-Stale: true`.
- **No runs in the database:** 200 with empty lists. `/api/meta` then reports 0 runs, and the dashboard shows "No data yet: the first collection runs Tuesday or Friday at 07:00".
- **Unhandled errors:** 500 problem details without stack traces. The error is logged and traced.
- **Dashboard:** loading, empty and error states on every screen. An error shows the API's message.

## Observability

- `ServiceDefaults` adds OpenTelemetry tracing and metrics for ASP.NET Core, HttpClient and the runtime, plus Npgsql command tracing through the Aspire Npgsql EF Core integration.
- There's a health endpoint at `/health`, with a database check.
- Custom telemetry: an `ActivitySource` named `Tracker.MarketData` with a `LoadMarketData` span tagged with the run count and posting count, and a counter `tracker.market_data.reloads`.
- Everything is visible in the Aspire dashboard (traces, structured logs, metrics).

## Dashboard (React)

- **Pages (React Router):** `/` Overview, `/skills`, `/skills/:name`, `/salaries`, `/postings`.
- **Filter bar:** shared across pages. Its state lives in the URL query string, so back/forward and bookmarks work.
- **Data fetching:** TanStack Query with the filters as the query key. The API client is typed with TypeScript types that mirror the API's JSON.
- **Charts:** Recharts, with bars for skill demand, a histogram and percentile bars for salaries, and a weekly line when trends exist.
- **Visual identity:** the report's palette and type (Archivo / Source Sans 3 / IBM Plex Mono). Light and dark follow the OS setting, and it works at laptop and phone widths.
- **Accessibility:** keyboard-reachable controls and visible focus. Charts have text equivalents (the table beside or below them).

## Testing

1. **Refactor gate:** the full existing suite passes after the move, and the report is byte-identical except for the generated time.
2. **`Api.Tests` unit tests** for filters (parsing and validation, defaults from the profile, window maths), skill queries (shares, the with/without salary comparison, co-occurrence), salary queries (buckets, cells) and posting queries (search, sort, paging).
3. **`Api.Tests` integration tests:**
   - `WebApplicationFactory` against a Testcontainers Postgres filled by `RebuildService` from phase 1's `FixtureArchive`;
   - each endpoint returns the expected data;
   - a 400 on bad filters and a 404 for an unknown skill;
   - a 503 when the connection string points at a closed port.
4. **Dashboard:**
   - Vitest + React Testing Library for URL ↔ filter conversion, the skill table (sorting, empty state) and the error banner;
   - `tsc --noEmit` and `vite build` must pass.
5. **Live check:**
   - run the AppHost against the real database;
   - open every page;
   - change filters and confirm the numbers match the published report for the same filters (the 30-day window and target families);
   - confirm a trace for an API request, with its SQL spans, shows up in the Aspire dashboard.

## Decision notes to add

- **0007:** .NET Aspire for local orchestration and OpenTelemetry. The alternative was Docker Compose alone.
- **0008:** a shared `Tracker.Core` and an in-memory analysis API. The alternatives were per-endpoint SQL or a static JSON export.
- **0009:** React + TypeScript + Vite + TanStack Query. The alternatives were Next.js and Blazor.

## Open items (resolved during planning)

- Exact Aspire 13.5 APIs: `AddViteApp` signature, how the Vite proxy reads the injected API URL, and the Aspire project templates (`dotnet new aspire-apphost`, which needs `Aspire.ProjectTemplates` installed).
- Pinned npm package versions (React 19, Vite, TanStack Query, React Router, Recharts, Vitest).
- The histogram bucket edges and the maximum number of postings returned per endpoint are fixed above; the planning step confirms the payload size stays small (under 500 KB).
