# Job Market Tracker

A local tool for a software job search. Twice a week it collects job postings for one job market, works out which
skills employers ask for and what they pay, and compares that with your own skills. A dashboard lets you explore the
market, keep track of every application, and produce a CV tailored to each posting from your own true bullets.

It runs on your computer. Collected data, your skill profile, your CV and your API keys stay there and never go into
git.

> **Setting it up with an AI coding assistant?** Point it at [`AGENTS.md`](AGENTS.md). It walks the assistant
> through every step and tells it what to ask you (your Adzuna key, your skills, your region) and what never to do
> with them.

## What it does

| Part | What you get |
|---|---|
| **Collector** (`src/Archiver`) | Twice a week: [Adzuna](https://developer.adzuna.com/) search results, salary histograms and top companies; the public job boards of the companies you list (Greenhouse, Lever, Ashby, Workable, SmartRecruiters); Canada's [Job Bank](https://open.canada.ca/data/en/dataset/ea639e28-c0fc-48bf-b5dd-b8899bd43072) open data. Everything is saved raw, so history keeps building. |
| **Processor** (`src/Processor`) | Rebuilds a Postgres database from the whole archive: role family and seniority from titles, local or remote scope, salaries, about 180 skills, duplicates across sites, and a flag for "citizens only" or security-clearance roles. Then it writes a one-page HTML report. |
| **Report** | Skills in demand, your biggest gaps and strengths, salaries by role and seniority, best-matching open postings, who's hiring, weekly trends, and (for Ontario) where the jobs are by city. |
| **Dashboard** (`src/Dashboard`, `src/Api`) | A local web app over the same data: overview, skill explorer, salary explorer and a searchable postings list, with filters in the URL. |
| **Application tracker** | Shortlist postings, or paste jobs from LinkedIn or company sites. Then track status, dates, follow-ups, contacts, company notes and the documents you sent. A JSON backup restores it if the database is lost. |
| **CV tailoring** | For each posting, picks and orders bullets from your master CV that show what the posting asks for, shows which skills it covers, and generates a Word file. It never writes a sentence for you. |
| **Regions** | One file per job market. Ships with Toronto (GTA), all of Ontario, and Abu Dhabi. Add another city by writing one JSON file. |

Built with .NET 10 (minimal APIs, EF Core, Aspire, OpenTelemetry), PostgreSQL 17 in Docker, React 19 with
TypeScript and Vite, xUnit with Testcontainers, and Vitest. Every design decision has a short record in
[`docs/decisions/`](docs/decisions/).

## Quick start

You need [.NET 10 SDK](https://dotnet.microsoft.com/download), [Docker Desktop](https://www.docker.com/products/docker-desktop/),
[Node.js 22.12+](https://nodejs.org/) and a free [Adzuna API key](https://developer.adzuna.com/) (needed only for
regions that use Adzuna).

1. **Database.** Copy `.env.example` to `.env` and choose a password, then start Postgres:

       docker compose up -d --wait postgres

2. **Secrets.** These live in .NET user-secrets on your machine, never in files in the repo:

       dotnet user-secrets set "Adzuna:AppId"  "<your app id>"  --project src/Archiver
       dotnet user-secrets set "Adzuna:AppKey" "<your app key>" --project src/Archiver
       dotnet user-secrets set "ConnectionStrings:Tracker" "Host=localhost;Port=5434;Database=tracker;Username=tracker;Password=<your .env password>" --project src/Processor
       dotnet user-secrets set "ConnectionStrings:tracker" "Host=localhost;Port=5434;Database=tracker;Username=tracker;Password=<your .env password>" --project src/AppHost

3. **Your profile.** Copy `config/profile.example.json` to `config/profile.json` and set your level for each skill
   (`strong`, `working`, `learning` or `none`; names must match `config/skills.json`) and the role families you target.
   For CV tailoring, also copy `config/cv.example.json` to `config/cv.json` and replace it with your own true details.

4. **Your region.** `"Region"` in `config/sources.json` picks the job market: `ontario`, `toronto` or `abu-dhabi`. To
   use another city, see [Regions](#regions).

5. **First collection and report:**

       dotnet run --project src/Archiver -c Release -- --dry-run   # one request per source, into data/raw/_dryrun
       dotnet run --project src/Archiver -c Release                # the real collection (a few minutes)
       dotnet run --project src/Processor -c Release               # rebuild the database and write the report

   Open `data/reports/latest.html` in a browser.

6. **Dashboard:**

       cd src/Dashboard && npm ci && cd ../..
       dotnet run --project src/AppHost --launch-profile http       # on Windows: scripts\dashboard.cmd

   Then open http://localhost:5190. The console also prints a login link for the Aspire dashboard, which shows traces,
   logs and metrics.

## Running it twice a week

**Windows:** `powershell -File scripts/register-task.ps1` publishes both programs to `.artifacts/` and registers a
scheduled task for Tuesday and Friday at 07:00, which catches up after missed runs. The task runs
`scripts/run-pipeline.cmd` (collect, start Postgres, rebuild) and appends to `data/pipeline.log`. Re-run the script
after changing the code. In Docker Desktop, turn on "Start Docker Desktop when you sign in".

**macOS or Linux:** schedule the same three steps with cron. The rebuild runs even when the collection fails, as on
Windows. Cron has a short `PATH`, so give the full path to `dotnet` and `docker` if needed:

    0 7 * * 2,5  cd /path/to/job-market-tracker && { dotnet run --project src/Archiver -c Release; docker compose up -d --wait postgres; dotnet run --project src/Processor -c Release; } >> data/pipeline.log 2>&1

The project is developed and tested on Windows. The code is cross-platform .NET and Node, but macOS and Linux haven't
been tested yet; reports of problems are welcome.

New data appears in the dashboard within a minute of a rebuild.

**Exit codes.** The Archiver returns `0` when something was collected, `1` when every source failed or the run was
cancelled, and `2` for bad config. The Processor returns `0` for OK and `1` when processing failed (the previous
report is kept). It returns `2` for invalid config or profile, and `3` when it can't reach the database.

## Using the dashboard

- **Overview, Skills, Salaries, Postings:** filter by role family, seniority, local or remote, and time window. Every
  view can be bookmarked.
- **Tracker:** shortlist a posting from any market page, or choose **Paste a job** for jobs the collector never sees.
  Pasted jobs also go to `data/inbox/`, so the next rebuild counts them in the market data. Each entry has a status
  timeline, follow-ups (overdue ones show a count), contacts, company details and documents.
- **Tailor CV:** open an entry and choose **Tailor CV**. Tick and order the bullets, edit the headline and summary,
  and **Generate .docx**. The file is saved on the entry. If you edit it in Word, upload the final version there too.
  **Download full CV** on the Tracker page gives the untailored CV.

The tracker's tables are never touched by the twice-weekly rebuild. After every change the API also writes
`data/tracker/tracker.json` (uploaded files go to `data/tracker/files/`), and an empty database restores itself from
it. Tracker changes are accepted only from the dashboard on localhost.

## Configuration

| File | What it holds |
|---|---|
| `config/sources.json` | The active region; Adzuna search terms, paging and call budget (80 calls a run). |
| `config/regions/<name>.json` | Everything about one job market: places, currency, boards, Adzuna and Job Bank settings. |
| `config/skills.json` | The skills to look for, with their aliases. |
| `config/processor.json` | The analysis window (30 days). |
| `config/profile.json` | **Yours, never committed:** your skill levels and target role families. |
| `config/cv.json` | **Yours, never committed:** your master CV for tailoring. |
| `.env` | **Yours, never committed:** the Postgres password. |

Changes to skills, profile or region apply to all history at the next rebuild. Restart the dashboard after editing
`skills.json` or `profile.json`; it reads `cv.json` on every request.

## Regions

One region is active at a time ([ADR 0015](docs/decisions/0015-regions.md)):

| Region | Local area | Sources | Pay |
|---|---|---|---|
| `ontario` | All of Ontario, plus remote roles open to Canada | Adzuna (province-wide), 52 company job boards, Job Bank (all Ontario economic regions), pasted jobs | CAD, yearly or hourly |
| `toronto` | GTA municipalities, plus remote roles open to Canada | Adzuna (Toronto, 50 km), about 40 company job boards, Job Bank, pasted jobs | CAD, yearly or hourly |
| `abu-dhabi` | Abu Dhabi emirate, plus remote roles open to the UAE | 15 company job boards (mostly Dubai roles), pasted jobs | AED, usually monthly |

- **Ontario** counts any location in the province and adds a "Where the jobs are" section comparing the Toronto area,
  Ottawa, Kitchener-Waterloo, London, Hamilton and the rest of Ontario ([ADR 0016](docs/decisions/0016-province-wide-region.md)).
- **Abu Dhabi:** Adzuna has no UAE market and Job Bank is Canadian, so the region uses public boards and the jobs you
  paste (from LinkedIn, Bayt or GulfTalent), which are its main source.

**Switching:** set `"Region"` in `config/sources.json`, run one collection and rebuild, and restart the dashboard.
Every collection and every pasted job records its region, and a rebuild reads only the active region's, so several
regions can share `data/`. Until the rebuild, the dashboard warns that its data belongs to the previous region. The
application tracker is shared by every region.

**Adding a city:** copy a region file to `config/regions/<name>.json` and edit it:
- `Name`: the same as the file name.
- Labels: `Title`, `LocalLabel` and `RemoteLabel`.
- Country: `CountryCode`, `CountryNames`, `Culture`, and `TimeZone` (an IANA name such as `Europe/London`).
- `Local.Places`: each place name as postings write it, and the place to show for it. Names shared with other
  countries go in `NeedsCountryContext`.
- `InCountry` and `Foreign`: words that place a location in the country or in another one, and short codes such as
  provinces or states after a comma.
- `Salary`: the currency, its signs, the words that confirm or rule out a range, whether monthly pay counts as
  salary, and a plausible yearly range.
- `CitizenshipPatterns`: phrases that restrict a role to citizens or nationals.
- `Adzuna`: leave it out where Adzuna has no market.
- `Boards`, `CompanyNames`, and `JobBank` (Canada only).
- For an area too large to list, such as a province, see the optional `Local.Anywhere`, `Local.Exclude` and
  `Local.Areas` in `ontario.json`.

Word lists are small regular expressions: plain words work as written, but escape a literal dot as `\.`. Run
`dotnet test --filter "FullyQualifiedName~RegionTests"` to check every region file loads. Skill and title matching is
English-only, so regions suit English-language job markets. Check that a board still answers with
`dotnet run --project src/Archiver -c Release -- --verify-boards`.

## Data sources and terms

- **Adzuna:** a free developer key with daily and monthly call limits. The collector stays within its budget (80 calls
  per run) and paces requests. Read [Adzuna's terms](https://developer.adzuna.com/); the report and dashboard credit
  Adzuna as "Jobs data partly by Adzuna".
- **Company job boards:** the public JSON endpoints that Greenhouse, Lever, Ashby, Workable and SmartRecruiters
  publish for each company's careers page. The collector doesn't scrape websites or log in anywhere
  ([ADR 0002](docs/decisions/0002-official-apis-only.md)).
- **Job Bank:** Government of Canada open data, under the Open Government Licence – Canada.
- **Pasted jobs:** what you paste yourself. Jobs from LinkedIn, Indeed and similar sites come in only this way.

The collected postings belong to their publishers. This tool keeps them on your computer for your own research.
Don't republish them.

## Tests

    dotnet test                                     # every .NET test; integration tests start Postgres with Testcontainers (needs Docker)
    dotnet test --filter "Category!=Integration"    # the fast ones, without Docker
    cd src/Dashboard && npm ci && npm test && npm run build

## Project layout

    config/          sources, regions, skills and examples of your private files
    src/Archiver     the collector (console)
    src/Processor    rebuild and report (console)
    src/Tracker.Core interpretation, analysis, database, report (shared library)
    src/Api          the dashboard's API: market queries, application tracker, CV tailoring
    src/Dashboard    the React dashboard
    src/AppHost      Aspire host that runs the API and dashboard together
    tests/           xUnit test projects
    docs/decisions/  why each design choice was made
    docs/superpowers/ the specs and plans each phase was built from
    scripts/         Windows scheduled task, pipeline and dashboard launchers
    data/            everything collected and generated (gitignored)

## Licence

[MIT](LICENSE) © 2026 Abdulrahim Naser Eddin
