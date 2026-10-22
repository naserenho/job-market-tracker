# Setting up Job Market Tracker: a guide for AI coding assistants

This file is for an AI assistant (Claude Code, Codex, Copilot, Cursor, or any other) helping a person set up this
repository. Follow the steps in order. Each one ends with a check; don't go on until it passes. Read `README.md` for
what the tool does.

## Rules that always apply

1. **Secrets never go into files in the repo.** The Adzuna app id and key and the database password go only into .NET
   user-secrets (`dotnet user-secrets set …`) and the gitignored `.env`. Never write them into `config/*.json`,
   `appsettings*.json`, scripts, docs, commit messages or logs. Don't print them back to the person after they give
   them to you.
2. **Never commit** `.env`, `config/profile.json`, `config/cv.json`, anything under `data/`, or `.artifacts/`. The
   `.gitignore` already excludes them; check `git status` before any commit.
3. **Ask, don't invent.**
   - The person's skill levels, target roles, region and CV content come from them.
   - Never add skills, employers, dates or achievements to `config/cv.json` that they didn't give you. The tool
     exists to keep the CV true.
4. **Respect the Adzuna quota.** The free key has daily and monthly limits, and one full collection uses up to 80
   calls. Don't run the full collector in a loop or "just to check". Use `--dry-run` (one request per source) to test.
5. **Don't scrape.** The collector uses public JSON APIs only. Don't add browser automation or HTML scraping
   ([ADR 0002](docs/decisions/0002-official-apis-only.md)).
6. **Commands run from the repository root** unless a step says otherwise.

## Step 1: Prerequisites

Check each tool and its version:

    dotnet --version          # 10.0.x or later
    docker --version          # Docker Desktop (or Docker Engine) must be running: `docker info` succeeds
    node --version            # 22.12 or later
    git --version

If something is missing, tell the person what to install and link the official download page. Don't install system
software without their agreement.

**Check:** all four commands print versions and `docker info` succeeds.

## Step 2: The database

1. Copy `.env.example` to `.env`.
2. Generate a random password of at least 24 letters and digits, or ask the person to choose one. Put it in `.env` as
   `POSTGRES_PASSWORD=…`. Keep it in a shell variable for step 3 rather than retyping it.
3. Start Postgres:

       docker compose up -d --wait postgres

Postgres listens on `127.0.0.1:5434` (not 5432, to avoid clashing with a local install). If 5434 is taken, change the
port in `docker-compose.yml` and use the new port in every connection string below.

**Check:** `docker compose ps` shows `jobtracker-postgres` as healthy.

## Step 3: Connection strings (user-secrets)

The Processor and the dashboard host each read their own user-secret. Use the password from step 2:

    dotnet user-secrets set "ConnectionStrings:Tracker" "Host=localhost;Port=5434;Database=tracker;Username=tracker;Password=<password>" --project src/Processor
    dotnet user-secrets set "ConnectionStrings:tracker" "Host=localhost;Port=5434;Database=tracker;Username=tracker;Password=<password>" --project src/AppHost

Note the capital `T` for the Processor and lower-case `t` for the AppHost: they are different keys.

**Check:** `dotnet user-secrets list --project src/Processor` shows a `ConnectionStrings:Tracker` entry. Don't echo the
value.

## Step 4: The Adzuna key (only for regions that use Adzuna)

`ontario` and `toronto` use Adzuna; `abu-dhabi` doesn't (skip this step for it).

1. Ask the person whether they already have an Adzuna developer account. If not, tell them to register at
   https://developer.adzuna.com/ (free). After signing in, their developer dashboard shows an **Application ID** and an
   **Application Key**. Ask them to read Adzuna's terms of service.
2. Ask the person to paste the id and key to you, or to run the commands themselves:

       dotnet user-secrets set "Adzuna:AppId"  "<application id>"  --project src/Archiver
       dotnet user-secrets set "Adzuna:AppKey" "<application key>" --project src/Archiver

3. Environment variables `ADZUNA__APPID` and `ADZUNA__APPKEY` also work, for example on a server. Never put the key in
   `config/sources.json`.

**Check:** `dotnet user-secrets list --project src/Archiver` lists both keys. Don't print their values.

## Step 5: The person's profile and CV

1. Copy `config/profile.example.json` to `config/profile.json`.
2. **Target roles.** Ask which kinds of roles they want. The choices are `dotnet`, `fullstack`, `software_engineer`,
   `lead_manager` and `other`. Set `targetFamilies`.
3. **Skill levels.** Ask for their level in the skills that matter to them: `strong`, `working`, `learning` or `none`.
   - Names must match a `"name"` in `config/skills.json` exactly.
   - Offer the list grouped by category instead of asking about all ~180.
   - Skills left out count as `none`, which is how gaps are found.
4. **Optional master CV.** Only if they want CV tailoring:
   - Copy `config/cv.example.json` to `config/cv.json` and fill it from the CV they give you.
   - Each bullet must be something they actually did; ask when unsure.
   - The comments at the top of the example explain the format.

**Check:** the dashboard step later lists any problem in `cv.json`. For the profile, step 7's Processor run fails with
exit code 2 and a message naming the field if something is wrong.

## Step 6: The region

`"Region"` in `config/sources.json` chooses the job market. Ask where the person is searching.

- **Toronto area:** `toronto`.
- **Anywhere in Ontario,** comparing cities: `ontario`.
- **Abu Dhabi:** `abu-dhabi`.
- **Another city, English-language market:** write a new region file. Follow README → Regions → "Adding a city":
  1. Copy the closest file in `config/regions/`.
  2. Set `Name` to the new file name.
  3. Fill in places, country words, currency, citizenship phrases and job boards for companies hiring there.
  4. Check whether Adzuna covers the country (its API supports about 16 countries, for example `gb`, `us`, `au`,
     `ca`, `de`, `fr`, `in`, `nz`, `sg` and `za`). Leave the `Adzuna` section out if it doesn't.

To find job boards, look up companies hiring in that city and check whether their careers page uses Greenhouse,
Lever, Ashby, Workable or SmartRecruiters. Add each as `{ "Ats": "<type>", "Slug": "<company slug>" }`.

**Check:**
- `dotnet test tests/Tracker.Core.Tests --filter "FullyQualifiedName~RegionTests"` passes; it loads every region file.
- With network access, `dotnet run --project src/Archiver -c Release -- --verify-boards` shows the boards answering.

## Step 7: First collection and report

    dotnet run --project src/Archiver -c Release -- --dry-run    # one request per source → data/raw/_dryrun
    dotnet run --project src/Archiver -c Release                 # full collection, a few minutes
    dotnet run --project src/Processor -c Release                # rebuild + report

**Check:**
- The Archiver exits 0 and prints a run id. `data/raw/<run>/manifest.json` lists each source's outcome.
- The Processor exits 0 and logs "Report written … postings in the window". Open `data/reports/latest.html`.
- If the Processor exits 3, Postgres isn't reachable: check step 2 and the connection string's password and port.

## Step 8: The dashboard

    cd src/Dashboard && npm ci && cd ../..
    dotnet run --project src/AppHost --launch-profile http      # Windows shortcut: scripts\dashboard.cmd

**Check:**
- http://localhost:5190 shows the region's title and the latest run.
- The Tracker page loads.
- If the person made a `cv.json`, its status shows on the Tracker page.

## Step 9: Twice-weekly runs

- **Windows:** `powershell -File scripts/register-task.ps1` publishes the programs and schedules Tuesday and Friday at
  07:00. Re-run it after any code change. Ask the person before registering a scheduled task.
- **macOS or Linux:** offer the cron line from README → "Running it twice a week". Add it only if the person agrees.

**Check:** the scheduled run appends to `data/pipeline.log`.

## Step 10: Verify the whole thing

    dotnet test                                            # needs Docker (integration tests use Testcontainers)
    cd src/Dashboard && npm test && npm run build && cd ../..

Report the counts to the person.

## Changing the code

- Read the relevant `docs/decisions/` record before changing a design. Add a new record for a new decision.
- **Tests come first:** write the test, see it fail, then make it pass. Keep the build at 0 warnings.
- **Database changes.** The market tables belong to the Processor (`TrackerDb`); add a migration with
  `dotnet ef migrations add <Name> --project src/Tracker.Core --output-dir Data/Migrations`. The tracker tables belong
  to the API (`ApplicationsDb`), which has its own migrations.
- **Never change the archive format** in a way that stops old runs from reading: every rebuild re-reads all history.
- **Behaviour changes for a shipped region** (`toronto`, `ontario`, `abu-dhabi`) need a reason. Compare a rebuild
  before and after the change.
