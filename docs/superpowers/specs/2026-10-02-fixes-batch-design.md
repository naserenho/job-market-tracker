# Fixes batch: design

Date: 2026-10-02. Branch: `feat/fixes-batch`.

## Goal

Clear the minor issues deferred by the final reviews of phases 1, 2a, 2b and 2c. No new features. Every fix is pinned
by a test that fails first.

The owner approved this scope on 2026-10-02: fix everything below, skip the four items under "Not in this batch".

## Constraints carried forward

- Secrets stay in .NET user-secrets or environment variables. `.env`, `config/profile.json`, `config/cv.json`, `data/`
  and `.artifacts/` are never committed, read aloud or logged.
- Tests and reviewers never run the Archiver and never touch the network.
- `ApplicationsDb`, its migrations and the format of `tracker.json` do not change. No fix needs a migration.
- The build stays at 0 warnings.

## A. Fixes that change the market numbers

### A1. Software titles with a specialty qualifier

Today `TitleRules.Family` drops any software title that also matches `NotSoftwareEngineer`, so
"Senior Software Engineer, Security" is Other.

After: the exclusions split in two.

- **Always excluded:** business development, sales, support, QA, test, hardware, mechanical, electrical, field engineer.
- **Excluded only without an explicit software title:** security, site reliability, SRE, DevOps, data engineer,
  machine learning, ML engineer, solutions engineer, network.

An explicit software title is `software engineer(s)`, `software developer(s)`, `software development engineer` or
`software dev engineer`. The software-title rule also accepts the plural ("Software Engineers").

| Title | Before | After |
|---|---|---|
| Senior Software Engineer, Security | Other | SoftwareEngineer |
| Staff Software Engineer, Machine Learning Platform | Other | SoftwareEngineer |
| Canada- Jr Software Engineer (SRE) | Other | SoftwareEngineer |
| Software Engineers (5) | Other | SoftwareEngineer |
| Senior Software Engineer in Test | Other | Other |
| Product Security Engineer | Other | Other |
| Senior Developer (Windows), Product Security | Other | Other |
| Software Developer and IT Support | Other | Other |

### A2. Company-name variants

Today the report's top companies, the API's company list, the skill page's companies and the `company` filter compare
trimmed names, so "Acme" and "Acme Inc." are two companies.

After: one shared key, `CompanyName.Key(name)` in Tracker.Core. It is `Deduplicator.Normalize(name)` (lower case,
punctuation dropped, the words inc, ltd, llc, corp, corporation, limited, co, company and the dropped); when that is
empty, the trimmed lower-case name. Every grouping and the `company` filter use the key. The name shown for a group is
its most frequent spelling, ties broken by ordinal order. A `company` filter value therefore matches every variant, and
surrounding spaces no longer matter (this also closes the 2a "untrimmed company filter" item).

The tracker's own company records (`Rules.CompanyKey`) are not touched.

### A3. Pasted postings age out

Today every file in `data/inbox` counts as seen in the latest run for as long as it exists.

After: a file with a readable `Saved: yyyy-MM-dd` line is seen once, in the first run that started on or after that
date (local date of the run's start); if every run is earlier, the latest run. It then leaves the window like any
collected posting. A file without a readable `Saved` line keeps today's behaviour and adds the warning
"Inbox file X has no 'Saved: yyyy-MM-dd' line, so it never ages out of the market data."

Consequences, all intended: a pasted job stops being "active now" after the next collection; tracker entries are
untouched, and their job text still resolves because the posting row stays in the database.

Dashboard wording follows: for an entry whose posting id starts with `inbox:`, the facts line says
"pasted {date}" instead of "still live / not in the latest run", and the tracker list never shows "Live" or "gone"
for it.

### A4. Comma-joined enum values

`Enum.TryParse("Applied,Offer")` reads a comma list as a bitwise OR, which can land on a defined value. After:
`Rules.ParseEnum`, `FilterParser.ParseEnum`, `ParseFamily` and `ParseSeniority` refuse any single value containing a
comma with their normal 400. Lists that are split on commas first (`status=`, `families=`, `seniority=`) keep working.

### Checking the numbers after the merge

The old check ("907 postings, top gap AI (general), unchanged") no longer applies. The new check, run on the
owner's machine with the published Processor (never the Archiver):

1. Before merging, run `--report-only` from main and note the in-window total B.
2. After merging, run a full rebuild. The new total must equal B, plus the in-window, in-scope, non-duplicate postings
   whose family changed from Other under A1, minus pasted postings that A3 moved out of the window.
3. Both counts come from SQL against the rebuilt database and are reported to the owner with the titles that moved.

## B. Fixes that keep the numbers

### Processor

| # | Today | After | Pinned by |
|---|---|---|---|
| B1 | Any `NpgsqlException` during migration exits 3 "database unavailable" | Exit 3 only when the database is unreachable as configured (connection, timeout, login, missing database, starting up: the API's `DatabaseErrors.IsUnavailable` rule, moved to Tracker.Core and shared). Other database errors exit 1 with "Database error: …" | Unit tests of the shared rule |
| B2 | No tests for `--report-only` or the exit codes | The program body moves to `ProcessorApp.RunAsync(args, root, connectionString, stdout, stderr, ct)`; `Program.cs` only finds the root and connection string. Tests cover: unknown argument 2, missing profile 2, missing connection string 2, unreachable database 3, `--report-only` on an empty database 1, rebuild then `--report-only` 0 with a report written | Integration tests on the Postgres fixture and the fixture archive |

### API, market side

| # | Today | After | Pinned by |
|---|---|---|---|
| B3 | A reload that fails for a non-database reason is retried on every request | With data already loaded, any reload failure is logged, marks the data stale and waits the normal 60 s before the next try. A cold start still fails the request | `MarketData` test with a throwing source and a fake clock |
| B4 | The caller's disconnect cancels the reload other requests share | The reload runs under the application's stopping token, not the request's. The caller's token only bounds its own wait for the gate | `MarketData` test: cancel the first caller mid-load, the second still gets data |
| B5 | Unknown `skill`: `/salaries` 400, `/postings` empty | `/postings` also answers 400 "Unknown skill" (parameter `skill`). `/skills/{name}` stays 404: there the skill is the resource | Endpoint test |

### API, tracker side

| # | Today | After | Pinned by |
|---|---|---|---|
| B6 | A NUL character in any text field but the job text gives a 500 | `Rules.Required` and `Rules.Optional` refuse NUL with the field's 400 | Rules test and one endpoint test |
| B7 | A backup file that can't be opened for permissions, or whose rows the database refuses on restore, gives a generic 500 | Both become `TrackerBackupException` naming the file; the tracker stays closed | Restore tests |
| B8 | The backup write has no trace span | `tracker.backup` activity around the write, with an error status when it fails | Activity listener test |
| B9 | Two saves creating the same new company at once: one gets a 409 without an id | The loser retries once against the company the winner created and succeeds. A shortlist race on the same posting gives 409 with the existing entry's id | Two-context test |
| B10 | After the tracker tables disappear while the API runs (lost volume), every tracker request fails until restart | An undefined-table error from a tracker endpoint resets the ready flag and answers 503 "try again"; the next request migrates and restores from the backup | Integration test that drops the tables |
| B11 | The six reads for the backup copy are separate statements | They run in one REPEATABLE READ transaction | Interceptor test on the isolation level |
| B12 | An entry whose pasted job the rebuild excluded keeps saying it "joins after the next rebuild" | The entry detail gains `marketState`: `Counted`, `Pending` (no posting row yet), `OutOfScope`, `Duplicate`. The page shows a sentence for each | Endpoint test and page test |

### CV

| # | Today | After | Pinned by |
|---|---|---|---|
| B13 | A skill in the skills list never points at unticked bullets that prove it | "Listed" chips are buttons, like "Available" ones, when an unticked bullet shows the skill; the hint says so | Page test |
| B14 | A line break in the summary is a raw LF in one Word run | Each line break becomes a Word line break | `CvDocx` test reading the breaks back |
| B15 | A highlight stays after its skill moves to "In a bullet" | The highlight applies only while its skill is still a button | Page test |
| B16 | A bullet removed from `cv.json` while the page is open blocks Generate until reload | Choices are filtered to the proposal's bullets on every render; a refused Generate refetches the proposal and says how many bullets were dropped | Page test |
| B17 | A role or project whose end is before its start loads; a project end without a start is silently dropped | Both are problems with their paths | `MasterCvFile` tests |
| B18 | `CvDocx` writes unpaired surrogates | They are dropped from every run | `CvDocx` test with the validator |
| B19 | Three test gaps | Integration tests: duplicate bullet ids refused; a market entry with saved entry text proposes from the entry text; a path-like company name gives a safe file name | The tests themselves |

### Dashboard

| # | Today | After | Pinned by |
|---|---|---|---|
| B20 | With the API down behind the dev proxy, pages say "The API answered 500." | A 5xx without a problem-details body gives the "not reachable, is the dashboard running" message | `api.test.ts` |
| B21 | The postings search box ignores back/forward | The box follows the `q` in the address | `Postings.test.tsx` |
| B22 | "Page 3 of 2" when the data shrinks | The page number is clamped and the address corrected | `Postings.test.tsx` |
| B23 | Shortlist errors appear only as a tooltip | The message shows beside the button with `role="alert"` | `ShortlistButton.test.tsx` |
| B24 | Renaming a company to an existing name offers a Reload that can't help | Reload shows only for a version conflict (a 409 without an `id`) | `EntryPage.test.tsx` |
| B25 | The Overview page has no test | Tests for the loaded page and the empty state | `Overview.test.tsx` |
| B26 | Host and CORS protection rely on Vite's defaults | `server.host`, `server.allowedHosts` and `server.cors: false` are set explicitly | Config test |
| B27 | The SkillDetail comment says React Router keeps `%2F` encoded | Comment corrected; code unchanged | Review |

### Docs

| # | After |
|---|---|
| B28 | README Setup says to run `npm ci` in `src/Dashboard` before the dashboard tests. README and the phase notes describe A1–A3. ADR 0014 records the paste-ageing rule and the company key |

## Not in this batch

The owner chose to skip these; they stay as they are.

- Mixed naming styles in database values.
- Dates shown in UTC near midnight.
- `skills.json` and `profile.json` changes needing a dashboard restart.
- Report section order, and Job Bank showing only the latest month.

## Error handling

No new error shapes. New failures reuse the existing problem details: 400 with `field` or `parameter`, 409 with `id`,
503 "Database unavailable", 500 "Tracker backup unreadable".

## Testing

- .NET: xUnit, Testcontainers Postgres through the existing fixtures. Processor tests live in a new
  `tests/Processor.Tests` project and use `FixtureArchive`.
- Dashboard: Vitest with the existing render helper.
- Each fix: a failing test first, then the change, then the whole suite.
- One final review by a fresh reviewer on the most capable model, then one fix pass.
