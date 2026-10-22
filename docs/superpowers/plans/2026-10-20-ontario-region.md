# Ontario Region Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an `ontario` region whose local area is the whole province, with a report section comparing jobs by area, plus public Workable and SmartRecruiters job feeds if they pass their checks.

**Architecture:**
- **Optional fields only:** every new behaviour hangs off an optional region-file field. The shipped `toronto.json` and `abu-dhabi.json` are untouched, so Toronto rebuilds identically.
- **Scope:** `ScopeRules` gains a province-wide fallback after the place list.
- **Job Bank:** `JobBankReader` filters by province and also totals tech postings per economic region, stored in a new `jobbank_regions` table.
- **Report:** the `Analyzer` groups in-window postings into areas for a new report section, built only when the region defines `Local.Areas`.
- **Boards:** the new board types are each a URL, a reader and a validator entry.

**Tech Stack:** .NET 10, EF Core 10 + Npgsql, xUnit + Testcontainers.

**Spec:** `docs/superpowers/specs/2026-10-19-ontario-region-design.md`

## Global Constraints

- **Toronto compatibility is pass/fail** (spec guarantees 1–4):
  - every existing test keeps its assertions, except the mechanical `.Months` change named in Task 5;
  - `toronto.json` and `abu-dhabi.json` are not edited.
- **Work only in the worktree** `../job-market-tracker-ontario`, on branch `feat/ontario-region`.
  - The main checkout stays on `main`: the Tue/Fri 07:00 scheduled task runs `.artifacts\*.exe` with the main checkout's `config/`.
  - Never edit files there or run `register-task.ps1` before the owner approves the merge (Task 10).
- **Secrets:**
  - they stay in user-secrets;
  - connection strings are built in the shell and never printed;
  - never read, print or commit `.env`, `config/profile.json`, `config/cv.json` or `data/` contents.
- **The owner's live database `tracker`:** never written. Comparisons use scratch databases in the same container (`jobtracker-postgres`, port 5434).
- **Network calls allowed, and nothing else:**
  - public board endpoints in Tasks 1 and 9, at most 1 request per second, User-Agent `job-market-tracker (personal research)`;
  - one Adzuna `--dry-run` in Task 4;
  - one full Archiver run into a scratch data folder in Task 10.
- **Build hygiene:**
  - 0 build warnings;
  - commit per task with the session's attribution lines;
  - region JSON keeps PascalCase names, `//` comments, and rejection of unknown members.
- **Scratch work** goes in the session scratchpad (`<scratch>` below), never the repo.

## Review Focus

1. **`Ontario, CA` is ambiguous.** In "Ontario, CA, USA" or "Ontario, California" it's the US city, and must not be local. In "Toronto, Ontario, CA", CA means Canada, and Toronto stays local. Tests in Task 3.
2. **Location text that is only the province:** "Ontario, Canada", "ON, Canada" or "Remote - Ontario". These must be local with city `Ontario`, never a city named "Canada" or "Remote". Tests in Task 3.
3. **Adzuna's reversed area segments** for a town not in the place list: "Canada, Ontario, Timmins" must give city Timmins, not Canada. Test in Task 3.
4. **Job Bank cell values:** names with en dashes (`Kitchener–Waterloo–Barrie Region`), and `NA` economic regions, must be kept as written and totalled, not dropped or merged. Test in Task 5.
5. **An area with no postings in the window** must still appear, with 0. A city with no jobs is an answer, not a missing row. Test in Task 6.

---

### Task 0: Worktree, spec commit and Toronto baseline

**Files:**
- Commit: `docs/superpowers/specs/2026-10-19-ontario-region-design.md`, this plan

- [ ] **Step 1: Commit the spec and plan.** The worktree already exists on `feat/ontario-region`.
  `git add docs/superpowers && git commit -m "Spec and plan: Ontario region"`
- [ ] **Step 2: Build the scratch root.** Create `<scratch>/compat/root`:
  - `config/`: a copy of the worktree's `config/`, plus the owner's `config/profile.json` copied in (never printed);
  - `data/raw`, `data/inbox`, `data/jobbank`: directory junctions (`mklink /J`) to the main checkout's `data/` folders;
  - `data/reports`: an empty folder.
- [ ] **Step 3: Rebuild Toronto on `main`'s code into a scratch database.**
  - `docker exec jobtracker-postgres createdb -U tracker tracker_compat_base`
  - From `<scratch>/compat/root`, run `dotnet run --project <worktree>/src/Processor -c Release`.
  - Set `ConnectionStrings__Tracker` to the Processor's user-secret with `Database=tracker_compat_base`, built in the shell and never echoed.
  - Expected: exit 0, "Rebuilt from N runs".
- [ ] **Step 4: Export the baseline.**
  - **Postings:** `<scratch>/compat/base-postings.tsv`, via `psql -At -F '<tab>'`. Columns: id, role_family, seniority, in_scope, is_local, is_remote_in_country, city, salary_min, salary_max, salary_period, salary_origin, citizenship_or_clearance, duplicate_of, sighting run ids (`string_agg` ordered), skill names (`string_agg` ordered). Ordered by id.
  - **Job Bank:** `<scratch>/compat/base-jobbank.tsv` from `jobbank_monthly`, ordered by month and noc_code.
  - **Report:** copy `data/reports/latest.html` to `<scratch>/compat/base-report.html`.

---

### Task 1: Check the new board types and find Ontario boards

No repo files change. The output is `<scratch>/boards/ledger.md`, which Tasks 7, 8 and 9 read.

- [ ] **Step 1: Write `<scratch>/boards/probe.py`.** For each candidate slug it calls every board type once:
  - `https://boards-api.greenhouse.io/v1/boards/<slug>/jobs`
  - `https://api.lever.co/v0/postings/<slug>?mode=json`
  - `https://api.ashbyhq.com/posting-api/job-board/<slug>`
  - `https://apply.workable.com/api/v1/widget/accounts/<slug>?details=true`. The documented `www.workable.com/api/accounts/<slug>` answers 302 to this; follow redirects.
  - `https://api.smartrecruiters.com/v1/companies/<slug>/postings?limit=100`. This answered 200 without a key on 2026-10-20.

  For each it prints the slug, board type, HTTP status, job count, and the count of jobs whose location mentions Ottawa, Kanata, Waterloo, Kitchener, Cambridge, Guelph or London (with Ontario/ON).

  Candidates to probe (guesses, verified only by the probe): shopify, ciena, kinaxis, fullscript, assent, solace, mitel, rossvideo, mindbridge, tealbook, coconut, you.i, l-spark, opentext, vidyard, applyboard, arcticwolf, auvik, bonfire, faire, clearpath, ottomotors, sortable, thalmic, north, axonify, magnetforensics, eSentire, dematic, digitalextremes, startech, voices, diply, northerncommerce, carfax, knowroaming, trojan, nokia, blackberry, qnx, ansys.

  Try each slug in lower case, and as written.
- [ ] **Step 2: Decide each new board type.** A type passes only when:
  - (a) its endpoint answered 200 with jobs and no key;
  - (b) at least 3 different candidates on it have at least 1 job in Ottawa, Kitchener-Waterloo (incl. Cambridge, Guelph) or London.
  
  Record in the ledger:
  - Workable: pass or fail, with the evidence;
  - SmartRecruiters: pass or fail, with the evidence, and whether `country=ca` filters the list server-side (compare `totalFound` with and without it on one passing company);
  - the boards to add, on any of the five types, that have at least 1 job in Ontario.
- [ ] **Step 3: Save sample responses.** Keep one real response per passing new type as `<scratch>/boards/sample-<ats>.json`, and for SmartRecruiters also one `.../postings/<id>` detail response. Tasks 7 and 8 trim these into test fixtures.

---

### Task 2: Optional region fields and their validation

**Files:**
- Modify: `src/Tracker.Core/Configuration/Region.cs`, `src/Tracker.Core/Configuration/ProcessorOptions.cs` (`JobBankOptions`)
- Test: `tests/Tracker.Core.Tests/Configuration/RegionTests.cs`

**Interfaces:**
- **`RegionLocal`** gains optional fields (no `[JsonRequired]`; absent means today's behaviour):
  - `RegionMarkers? Anywhere`: words and codes that make any location segment in the area local;
  - `List<string>? Exclude`: patterns that rule a segment out of the `Anywhere` fallback unless the posting's structured country is the region's;
  - `Dictionary<string, string>? Areas`: the place shown → its area, for the report.
- **`JobBankOptions`** gains `string Province = ""`.
- **`RegionAdzuna.DistanceKm`** may be 0, meaning "not sent".

- [ ] **Step 1: Failing tests** in `RegionTests`. Each loads a temp copy of `toronto.json` edited with `System.Text.Json.Nodes`:
  - `Toronto_and_Abu_Dhabi_have_no_province_wide_fields`: `RepoConfig.Toronto.Local.Anywhere`, `.Exclude` and `.Areas` are null, and `RepoConfig.Toronto.JobBank!.Province == ""`.
  - `Job_bank_needs_exactly_one_of_economic_region_or_province`:
    - both set → `InvalidDataException` whose message contains `"JobBank: set exactly one of EconomicRegion or Province."`;
    - neither set → the same message;
    - only `Province: "Ontario"` → loads.
  - `Adzuna_distance_may_be_left_out_but_not_negative`:
    - `DistanceKm` removed → loads, with `Adzuna.DistanceKm == 0`;
    - `-5` → message contains `"Adzuna.DistanceKm must be at least 1 when set."`.
  - `Province_wide_lists_reject_empty_entries`:
    - `Local.Anywhere.Words: [""]` → message contains `"Local.Anywhere.Words has an empty entry."`;
    - `Local.Exclude: ["("]` → message contains `"Local.Exclude: '(' is not a valid pattern."`;
    - `Local.Areas: {"Ottawa": ""}` → message contains `"Local.Areas has an empty place or area."`.
- [ ] **Step 2:** Run `dotnet test tests/Tracker.Core.Tests --filter RegionTests`. Expected: the new tests FAIL.
- [ ] **Step 3: Implement.**
  - **The new fields:** add them, with comments in the file's style.
  - **`Validate`:**
    - replace the `DistanceKm < 1` rule with `< 0`, using the message above;
    - add the JobBank rule when `r.JobBank` is not null;
    - add `Anywhere.Words`, `Anywhere.Codes` and `Exclude` to the empty-entry check;
    - add `Anywhere.Words` and `Exclude` to the pattern check;
    - add the Areas check.
- [ ] **Step 4:** Run `dotnet test tests/Tracker.Core.Tests`. Expected: all PASS.
- [ ] **Step 5: Commit.** `"Region file: optional province-wide fields, Job Bank province, optional Adzuna distance"`

---

### Task 3: Province-wide local scope and the Ontario file

**Files:**
- Create: `config/regions/ontario.json`
- Modify: `src/Tracker.Core/Interpreting/ScopeRules.cs`, `tests/Tracker.TestSupport/RepoConfig.cs` (add `Ontario`)
- Test: `tests/Tracker.Core.Tests/Interpreting/ScopeRulesOntarioTests.cs`, `tests/Tracker.Core.Tests/Configuration/RegionTests.cs`

**Interfaces:**
- Consumes: Task 2's fields.
- Produces:
  - `RepoConfig.Ontario: Region`;
  - `ScopeRules` classifies with the `Anywhere` fallback; its public API is unchanged.

- [ ] **Step 1: Write `config/regions/ontario.json`.** The header comment follows `toronto.json`'s, plus one line explaining `Anywhere`, `Exclude` and `Areas`.
  - **Copied from `toronto.json`, verbatim:** `CountryCode`, `CountryNames`, `Culture`, `TimeZone`, `InCountry`, `Foreign`, `CitizenshipPatterns`, `Boards` (all 39, final list in Task 9) and `CompanyNames`.
  - **Labels:** `Name` `ontario`, `Title` `Ontario Job Market`, `LocalLabel` `Ontario`, `RemoteLabel` `Remote (Canada)`.
  - **`Local.Places`:**
    - every `toronto.json` entry;
    - plus Ottawa, Kanata, Nepean, Orléans, Orleans and Gloucester → `Ottawa`;
    - Kitchener, Waterloo, Cambridge and Guelph, each → itself;
    - London, Hamilton, Windsor, Kingston, Barrie, Sudbury and St. Catharines, each → itself, and Greater Sudbury → `Sudbury`;
    - Niagara Falls, Peterborough, Brantford, Sarnia, Thunder Bay, Belleville and North Bay, each → itself.
  - **`Local.NeedsCountryContext`:** Toronto's list plus London, Cambridge, Waterloo, Hamilton, Kingston, Windsor, Ottawa, Peterborough, Gloucester, Belleville and Brantford.
  - **`Local.Anywhere`:** `{"Words": ["ontario"], "Codes": ["ON"]}`.
  - **`Local.Exclude`:** `["^ontario,\\s*(?:ca|california|or|oregon|ny|new york)", "ontario,\\s*california"]`. These are the US Ontarios in California, Oregon and New York.
  - **`Local.Areas`:**
    - every distinct value in `toronto.json`'s `Places` → `Toronto area (GTA)`;
    - `Hamilton` → `Hamilton`;
    - `Kitchener`, `Waterloo`, `Cambridge`, `Guelph` → `Kitchener-Waterloo`;
    - `London` → `London`;
    - `Ottawa` → `Ottawa`.
  - **`Salary`:** Toronto's, with `ContextWords` plus `ottawa`, `waterloo`, `kitchener`, `london,? ontario`, `hamilton`.
  - **`Adzuna`:** `{"Country": "ca", "Where": "Ontario"}`, with no `DistanceKm` (Task 4 confirms this).
  - **`JobBank`:** Toronto's `DatasetId`, `BackfillMonths` and `NocCodes`, with `"Province": "Ontario"` in place of `EconomicRegion`.
- [ ] **Step 2: Failing tests** in `ScopeRulesOntarioTests`, with `new ScopeRules(RepoConfig.Ontario)` and the `Posting(...)` helper copied from `ScopeRulesTests`.
  - **`Local_anywhere_in_Ontario` (theory):**

    | Location | Country | City |
    |---|---|---|
    | `Ottawa, ON` | | Ottawa |
    | `Kanata, Ontario, Canada` | | Ottawa |
    | `Waterloo, Ontario, Canada` | | Waterloo |
    | `London, ON` | | London |
    | `Sudbury, ON` | | Sudbury |
    | `Timmins, Ontario` | | Timmins |
    | `Ontario, Canada` | | Ontario |
    | `ON, Canada` | | Ontario |
    | `Remote - Ontario` | | Ontario |
    | `Canada, Ontario, Timmins` | `CA`, source `adzuna` | Timmins |
    | `Toronto, Ontario, CA` | | Toronto |
    | `Ontario, CA` | `CA` | Ontario |

    Asserts `IsLocal` and the city.
  - **`Not_local` (theory):** `London, UK`, `London, England`, `Waterloo, IA`, `Ottawa, KS`, `Ontario, CA`, `Ontario, CA, USA`, `Ontario, California`, `Ontario, OR`, `Ontario, NY`, `Vancouver, BC`, `Remote - Canada`, `Hybrid - on site`. Asserts `!IsLocal`.
  - **`Remote_in_canada_is_unchanged`:** `Remote - Canada` → `IsRemoteInCountry`.
  - **In `RegionTests`:**
    - `Ontario_file_loads_and_groups_every_gta_place`: `RepoConfig.Ontario` loads, and every value of `RepoConfig.Toronto.Local.Places` maps to `Toronto area (GTA)` in `RepoConfig.Ontario.Local.Areas`;
    - `Toronto_patterns_are_unchanged`: the existing pinned pattern tests still pass untouched.
- [ ] **Step 3:** Run `dotnet test tests/Tracker.Core.Tests --filter "ScopeRules|RegionTests"`. Expected: the Ontario tests FAIL.
- [ ] **Step 4: Implement in `ScopeRules`.**
  - **Patterns:** build `_anywhereWords` (`RegionPatterns.Words`, IgnoreCase), `_anywhereCodes` and `_exclude` (`RegionPatterns.Words`, IgnoreCase). Each is null when its field is absent.
    - `_anywhereCodes` is `RegionPatterns.Codes("(?:^|,|-)", ...)`, compiled **case-sensitive** like `_inCountryCodes`, so `Hybrid - on site` never matches. The `^` lets `ON, Canada` match, with `ON` at the start of a segment.
  - **`Classify`:** when `LocalCity(p)` is null and `_anywhereWords` is set, use `AnywhereCity(p)`. Return the first segment that:
    - isn't foreign (the same `p.Country` rule as `LocalCity`);
    - isn't matched by `_exclude` (skip that check when `p.Country == _country`);
    - matches `_anywhereWords` or `_anywhereCodes`.
  - **The city** is `CityPart(segment) ?? region.LocalLabel`. `CityPart` is the first comma-separated part, trimmed, that still has text once these are removed:
    - every match of the `Anywhere` words;
    - an `Anywhere` code standing alone;
    - a `CountryNames` entry;
    - the `InCountry` words;
    - the remote word;
    - then whitespace and `-–.`.
    
    So `Remote - Ontario` and `Canada` are skipped, and `Timmins` is kept. Truncate it to 80 characters like `FirstSegment`.
  - **Toronto** has no `Anywhere`, so this branch never runs for it.
- [ ] **Step 5:** Run `dotnet test`. Expected: all PASS, Toronto tests untouched.
- [ ] **Step 6: Commit.** `"Ontario region: province-wide local scope and region file"`

---

### Task 4: Adzuna without a distance, checked with one dry run

**Files:**
- Modify: `src/Archiver/Configuration/SourcesValidator.cs`, `src/Archiver/Sources/AdzunaQueryPlan.cs`
- Test: `tests/Archiver.Tests/Configuration/SourcesValidatorTests.cs`, `tests/Archiver.Tests/Sources/AdzunaQueryPlanTests.cs`

- [ ] **Step 1: Failing tests.**
  - `Url_leaves_out_distance_when_not_set`: with `DistanceKm = 0` and `Where = "Ontario"`, the GTA search URL contains `where=Ontario` and no `distance=`.
  - `Toronto_urls_are_unchanged`: the existing URL tests stay as they are.
  - **Validator:** `DistanceKm = 0` gives no error; `-1` gives `"Adzuna:DistanceKm must be at least 1 when set."`.
- [ ] **Step 2:** Run `dotnet test tests/Archiver.Tests`. Expected: the new tests FAIL.
- [ ] **Step 3: Implement.** In `Url`, add `distance` only when `o.DistanceKm > 0`. Change the validator rule to match.
- [ ] **Step 4:** Run `dotnet test tests/Archiver.Tests`. Expected: PASS.
- [ ] **Step 5: Dry run.** In the worktree only, set `"Region": "ontario"` in `config/sources.json`, then run `dotnet run --project src/Archiver -c Release -- --dry-run`. That's one Adzuna search, written to the worktree's own `data/raw/_dryrun`.
  - Tally the result locations by Ontario city.
  - **Pass:** results come from more than one Ontario city, and none are outside Ontario.
  - **If Adzuna errors, or returns places outside Ontario:** add `"DistanceKm": 300` to `ontario.json` and repeat once.
  - Record the outcome in `<scratch>/boards/ledger.md`.
  - Then set `"Region"` back to `"toronto"`. The final switch happens in Task 10.
- [ ] **Step 6: Commit.** `"Adzuna: distance is optional; Ontario searches the province"`. Include the `ontario.json` change if Step 5 needed one.

---

### Task 5: Job Bank by province, with totals per economic region

**Files:**
- Modify:
  - `src/Tracker.Core/Reading/JobBankReader.cs`
  - `src/Tracker.Core/Data/Entities.cs`, `TrackerDb.cs`, `TrackerTables.cs`, `RebuildWriter.cs`
  - `src/Tracker.Core/Rebuild/RebuildService.cs`
  - `src/Tracker.Core/Analysis/AnalysisLoader.cs`
  - `src/Tracker.Core/Reporting/ReportRenderer.cs` (the Job Bank note only)
- Create: migration `AddJobBankRegions` via `dotnet ef migrations add AddJobBankRegions --project src/Tracker.Core --startup-project src/Processor --output-dir Data/Migrations`
- Test: `tests/Tracker.Core.Tests/Reading/JobBankReaderTests.cs`, `Data/RebuildWriterTests.cs`, `Data/TrackerTablesTests.cs`, `Reporting/ReportRendererTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record JobBankRegionMonth(DateOnly Month, string EconomicRegion, int Postings, int Vacancies)`;
  - `public sealed record JobBankSummary(IReadOnlyList<JobBankMonth> Months, IReadOnlyList<JobBankRegionMonth> Regions)`;
  - `JobBankReader.Summarize(...)` and `SummarizeDirectory(...)` now return `JobBankSummary`;
  - `RebuildData` gains a last parameter `IReadOnlyList<JobBankRegionMonth>? JobBankRegions = null`;
  - table `jobbank_regions` (month, economic_region, postings, vacancies), with key (month, economic_region);
  - `TrackerTables.All` ends `..., jobbank_monthly, jobbank_regions`;
  - `AnalysisLoader.LoadJobBankRegionsAsync(TrackerDb db, CancellationToken ct) -> Task<IReadOnlyList<JobBankRegionMonth>>`.

- [ ] **Step 1: Failing tests.**
  - **`JobBankReaderTests`:** the existing assertions move to `.Months` unchanged. The test TSV gains a `Province/Territory` column, plus rows for `Ottawa Region`, `Kitchener–Waterloo–Barrie Region` (en dashes), `NA` and one Quebec row. Two tests:
    - `Province_filter_counts_every_ontario_region_and_totals_them`: with `Province = "Ontario"`, the NOC counts include the Ottawa and KWB rows and exclude Quebec. `Regions` has one row per region, with names exactly as written and `NA` kept, and postings per region equal to the tech-NOC rows in it.
    - `Economic_region_filter_is_unchanged`: with Toronto's options, `Months` is equal to the old result, and `Regions` is the single `Toronto Region` row.
  - **`RebuildWriterTests`:** `JobBankRegions` rows round-trip into `jobbank_regions`, and `null` writes none.
  - **`TrackerTablesTests`:** the existing order check covers the new table.
  - **`ReportRendererTests`:**
    - `Job_bank_note_names_the_province_when_filtered_by_province`: the Ontario note says `"... (NOC 2021) in Ontario."`;
    - Toronto's note text is unchanged: `"in the Toronto Region."`.
- [ ] **Step 2:** Run `dotnet test tests/Tracker.Core.Tests`. Expected: the new tests FAIL.
- [ ] **Step 3: Implement.**
  - **Reader:**
    - read the `Province/Territory` column;
    - match on it when `Province` is set, otherwise on `Economic Region` as today;
    - total matching rows per (month, economic region) in the same pass;
    - throw the existing `InvalidDataException` when the column the filter needs is missing.
  - **Database:** add the entity, `DbSet<JobBankRegionEntity> JobBankRegions`, `ToTable("jobbank_regions")` and the key, then the migration.
  - **Writer:** COPY the rows.
  - **`RebuildService`:** pass `summary.Months` and `summary.Regions`. The log line's Job Bank count stays `summary.Months.Count`.
  - **Report note:** `string.IsNullOrEmpty(jb.Province) ? $"the {jb.EconomicRegion}" : jb.Province`.
- [ ] **Step 4:** Run `dotnet test` (integration tests need Docker), then `dotnet ef migrations has-pending-model-changes --project src/Tracker.Core --startup-project src/Processor`. Expected: all PASS; no pending changes.
- [ ] **Step 5: Commit.** `"Job Bank: filter by province and total postings per economic region"`

---

### Task 6: "Where the jobs are" in the report

**Files:**
- Modify:
  - `src/Tracker.Core/Analysis/ReportModel.cs`, `Analyzer.cs`
  - `src/Tracker.Core/Reporting/ReportRenderer.cs`
  - `src/Processor/ProcessorApp.cs`
- Test: `tests/Tracker.Core.Tests/Analysis/AnalyzerTests.cs`, `tests/Tracker.Core.Tests/Reporting/ReportRendererTests.cs`, `tests/Processor.Tests/ProcessorAppTests.cs`

**Interfaces:**
- Consumes: `JobBankRegionMonth` and `AnalysisLoader.LoadJobBankRegionsAsync` (Task 5); `Region.Local.Areas` (Task 2).
- Produces:
  - `public sealed record AreaSettings(IReadOnlyDictionary<string, string> Map, string OtherArea, string RemoteArea, IReadOnlyList<JobBankRegionMonth> JobBank)`
  - `public sealed record AreaCount(string Area, int Postings, double Share, int WithSalary, decimal? MedianYearly)`
  - `public sealed record JobBankRegionTotal(string EconomicRegion, int Postings, int Vacancies)`
  - `public sealed record AreaBreakdown(IReadOnlyList<AreaCount> Areas, IReadOnlyList<JobBankRegionTotal> JobBank)`
  - `ReportModel` gains a last parameter `AreaBreakdown? Areas = null`.
  - `Analyzer.Build(...)` gains a last parameter `AreaSettings? areas = null`. The API call site is unchanged, and the dashboard gets no areas.
  - `Analyzer.MinAreaSalarySample = 10`.

- [ ] **Step 1: Failing tests.**
  - **`AnalyzerTests.Areas_group_window_postings_by_area`:** postings with cities Toronto, Mississauga, Ottawa and Sudbury, plus one remote. The map covers Toronto and Mississauga → GTA, and Ottawa → Ottawa, plus `London` → London with no postings.
    - Each area's count and share are of the window postings.
    - Sudbury goes to `OtherArea` ("Other Ontario").
    - The remote posting goes to `RemoteArea` ("Remote (Canada)").
    - London appears with 0.
    - Order: areas by postings descending then name, with the remote row last.
  - **`AnalyzerTests.Area_median_needs_ten_salaries`:** 9 salaried postings give `MedianYearly == null`; 10 give the median of `YearlyMidpoint`.
  - **`AnalyzerTests.Job_bank_region_totals_sum_all_months`:** per region, with postings descending.
  - **`AnalyzerTests.No_area_settings_means_no_areas`:** `Areas == null`.
  - **`ReportRendererTests.Area_section_renders_for_a_region_with_areas`:** the HTML contains `id="areas"`, the title `Where the jobs are`, a row per area with count and share, `—` where the median is null, and the Job Bank table by economic region. The note reads:
    `"Your target roles in the last {WindowDays} days, by area. A posting listing several cities counts once, under the first. Adzuna's province-wide search leans towards Toronto, so company boards and Job Bank carry the smaller cities."`
  - **`ReportRendererTests.Toronto_report_has_no_area_section`:** a model with `Areas == null` renders no `id="areas"`.
  - **`ProcessorAppTests`:** with the Ontario region the report contains `id="areas"`; with Toronto it doesn't.
- [ ] **Step 2:** Run `dotnet test`. Expected: the new tests FAIL.
- [ ] **Step 3: Implement.**
  - **`Analyzer`:** compute `AreaBreakdown` over the same `window` list the other sections use. A local posting's area is `Map.GetValueOrDefault(City ?? "") ?? OtherArea`; a posting that isn't local is in `RemoteArea`. Every map value and `OtherArea` appear even at 0.
  - **`ReportRenderer`:** an `AreaSection` placed right after `SnapshotRow`, rendered only when `m.Areas` is not null.
  - **`ProcessorApp`:** when `region.Local.Areas` is not null, pass `new AreaSettings(region.Local.Areas, $"Other {region.LocalLabel}", region.RemoteLabel, await AnalysisLoader.LoadJobBankRegionsAsync(db, ct))`.
- [ ] **Step 4:** Run `dotnet test`. Expected: all PASS.
- [ ] **Step 5: Commit.** `"Report: where the jobs are, by area and by Job Bank region"`

---

### Task 7: Workable board type (only if Task 1 passed it)

If the ledger says Workable failed, skip to Task 8 and record the failure in ADR 0016 (Task 9).

**Files:**
- Modify:
  - `src/Archiver/Sources/BoardUrls.cs`
  - `src/Archiver/Configuration/SourcesValidator.cs` (`KnownAts`, plus the message listing the types)
  - `src/Tracker.Core/Reading/SourceReaders.cs`, `Archive.cs` (dispatch)
- Create: `tests/Tracker.Core.Tests/Reading/Fixtures/workable.json`, trimmed from Task 1's sample to 2 jobs (one Ontario, one US) with real field names.
- Test: `tests/Archiver.Tests/Sources/BoardUrlsTests.cs`, `tests/Tracker.Core.Tests/Reading/SourceReadersTests.cs`, `tests/Archiver.Tests/Configuration/SourcesValidatorTests.cs`

**Interfaces:**
- Produces:
  - `SourceReaders.Workable(JsonElement root, string slug, SourceContext ctx) -> IEnumerable<RawPosting>`, with source `"workable"`;
  - `BoardUrls.For` gives `https://apply.workable.com/api/v1/widget/accounts/{slug}?details=true`.

- [ ] **Step 1: Failing tests.**
  - **URL:** the URL above.
  - **`CountJobs`:** the fixture gives 2.
  - **Validator:** `workable` is accepted; `recruitee` is still rejected.
  - **Reader**, on the fixture:
    - id = `shortcode`;
    - title;
    - company = `CompanyName(slug)`;
    - URL = the job's `url`;
    - location: its `locations` joined as `"city, region, country"` with `"; "` between them;
    - `Remote` = `telecommuting`;
    - `Country` = `ctx.Region.CountryCode` when the job's `country` or `countryCode` is in `CountryNames`;
    - description = plain text of `description`, with `IsFullText` true;
    - `Salary` null.
    
    Use the field names the saved sample actually has; if the sample differs from these names, the sample wins and the test pins it.
- [ ] **Step 2:** Run `dotnet test`. Expected: FAIL.
- [ ] **Step 3: Implement** the URL case, the `KnownAts` entry, the reader and the dispatch line.
- [ ] **Step 4:** Run `dotnet test`. Expected: PASS.
- [ ] **Step 5: Commit.** `"Boards: Workable public job feed"`

---

### Task 8: SmartRecruiters board type (only if Task 1 passed it)

If the ledger says SmartRecruiters failed, skip it and record the failure in ADR 0016 (Task 9).

**Files:**
- Modify:
  - `src/Archiver/Sources/BoardUrls.cs`, `JobBoardSource.cs`
  - `src/Archiver/Storage/RunFolder.cs` (detail file path)
  - `src/Archiver/Configuration/SourcesValidator.cs`
  - `src/Tracker.Core/Reading/SourceReaders.cs`, `Archive.cs`
- Create: fixtures `smartrecruiters.json` (a list with 3 postings: Ontario, remote Canada, US) and `smartrecruiters-detail.json`.
- Test: `JobBoardSourceTests`, `BoardUrlsTests`, `SourceReadersTests`, `SourcesValidatorTests`

**Interfaces:**
- **Archive layout:**
  - the list is saved as today, at `boards/smartrecruiters-<slug>.json`;
  - details go in `boards/smartrecruiters-<slug>/<id>.json`;
  - `RunFolder.BoardDetailFile(string ats, string slug, string id)`.
- **Reader:** `SourceReaders.SmartRecruiters(JsonElement list, string slug, Func<string, JsonElement?> detail, SourceContext ctx)`, with source `"smartrecruiters"`.

- [ ] **Step 1: Failing tests.**
  - **`JobBoardSourceTests`, with a fake HTTP handler:**
    - **Paging:** the list is paged at `limit=100` with `offset` until `totalFound`, capped at 10 pages, and the pages are saved merged as one list file.
    - **Details:** fetched only for postings whose `location.country` is the region's code in lower case, or whose `location.remote` is true with that country.
    - **Request count:** the US posting causes no detail request.
    - **Dry run:** fetches the first page only, and no details.
  - **`SourceReadersTests`:**
    - with the detail present: description = the plain text of the `jobAd.sections` texts in order (company description, job description, qualifications, additional information); `IsFullText` true;
    - with the detail missing: description "" and `IsFullText` false.
  - **Location, remote and country fields:** location = `"city, region, country"`; `Remote` = `location.remote`; `Country` from `location.country`, upper-cased and normalised through `CountryNames`.
  - **URL:** the posting's `postingUrl` from the detail, otherwise `https://jobs.smartrecruiters.com/{slug}/{id}`.
  - **Salary:** taken from the detail's compensation fields, only if the saved sample has them and the currency is the region's (as for Lever and Ashby). Otherwise null.
- [ ] **Step 2:** Run `dotnet test`. Expected: FAIL.
- [ ] **Step 3: Implement** the list paging and detail fetch in `JobBoardSource` for this type only. The other types keep one request each. Then the reader and the dispatch, which reads detail files from the subfolder.
- [ ] **Step 4:** Run `dotnet test`. Expected: PASS.
- [ ] **Step 5: Commit.** `"Boards: SmartRecruiters public posting feed, details for in-country postings"`

---

### Task 9: Ontario boards, docs and ADR 0016

**Files:**
- Modify: `config/regions/ontario.json` (`Boards`, `CompanyNames`), `README.md`
- Create: `docs/decisions/0016-province-wide-region.md`

- [ ] **Step 1: Add boards.** Add Task 1's boards to `ontario.json`, on any passing type, with display names in `CompanyNames`.
- [ ] **Step 2: Verify the boards.** In the worktree, with `"Region": "ontario"` temporarily, run `dotnet run --project src/Archiver -c Release -- --verify-boards`.
  - Expected: every board answers with a job count.
  - Remove any that don't, then set `"Region"` back to `"toronto"`.
- [ ] **Step 3:** Run `dotnet test --filter "FullyQualifiedName~RegionTests"`. Expected: PASS.
- [ ] **Step 4: README.**
  - **Regions table:** add the `ontario` row: "all of Ontario, plus remote roles open to Canada | Adzuna (province-wide), company boards, Job Bank (Ontario, all economic regions), pasted jobs | CAD".
  - **"Adding a city":** document `Local.Anywhere`, `Local.Exclude`, `Local.Areas`, `JobBank.Province` and the optional `Adzuna.DistanceKm`.
  - **Phase 0 source table:** add the kept board types.
  - **Switching:** say that the first Ontario rebuild has Job Bank's 12 months and one collection, and the window fills over about 4 weeks.
- [ ] **Step 5: ADR 0016, "A province-wide local area."** In the format of 0015:
  - **Context:** the place list can't name every town.
  - **Decision:**
    - the optional fields;
    - the `Exclude` rule for Ontario, California;
    - the Job Bank province filter and the region totals table;
    - the area section;
    - the board types kept or dropped, with Task 1's evidence, recorded as additions to ADR 0002's documented public sources;
    - Recruitee excluded, because its docs require a token.
  - **Alternatives:** a region per city; a full Ontario place list with no fallback; Adzuna searches per city (they split the 80-call budget).
  - **Consequences:** Adzuna leans towards Toronto; a posting listing several cities counts once, under the first; Ontario starts without Toronto's history.
- [ ] **Step 6: Commit.** `"Ontario boards; README and ADR 0016"`

---

### Task 10: Compatibility check, an Ontario trial and hand-over

- [ ] **Step 1: Toronto compatibility (guarantee 1).** The live `data/` folders change between Task 0 and now: scheduled runs, new Job Bank months, pasted jobs. So rebuild the baseline again at compare time, against the same archive, straight before the branch build. Task 0's baseline only served as an early smoke check.
  1. Make a baseline worktree with `git worktree add --detach ../jmt-baseline b0c7f88`.
  2. Rebuild it into `tracker_compat_base` (recreated) as in Task 0, then export the three baseline files again.
  3. Straight after, `createdb tracker_compat_branch`.
  4. Refresh `<scratch>/compat/root/config` from the branch worktree (keep the owner's profile).
  5. Run the branch's Processor the same way.
  6. Remove the baseline worktree with `git worktree remove ../jmt-baseline`.
  7. Export the same three files and compare:
     - **postings:** `diff` against the baseline, which must be empty, over the same row count;
     - **Job Bank:** `jobbank_monthly` must be identical;
     - **report:** must differ only on the masthead's "updated" line.
  
  Any other difference is a defect: fix it test-first, then repeat.
- [ ] **Step 2: Ontario trial.** Don't run it within an hour of the Tue/Fri 07:00 scheduled run: it writes into the real `data/jobbank` through a junction. Make `<scratch>/trial/root` with:
  - `config` from the worktree with `"Region": "ontario"`, plus the owner's profile;
  - its own empty `data/raw` and `data/inbox`;
  - `data/jobbank` as a junction to the real folder. The Archiver may add a new monthly file there, the same file the scheduled run would add.

  Then:
  1. Run the Archiver (full) from that root, then the Processor into `tracker_ontario_trial`.
  2. Expected: both exit 0, and the report has "Where the jobs are" with rows for Toronto area (GTA), Ottawa, Kitchener-Waterloo, London, Hamilton, Other Ontario and Remote (Canada), plus Job Bank by economic region.
  3. Copy the report to `<scratch>/trial/report.html` for the owner.
- [ ] **Step 3: Whole suite.**
  - Run `dotnet build` (0 warnings), then `dotnet test`. Expected: all PASS.
  - The dashboard is unchanged, so `npm test` isn't needed.
  - Drop the scratch databases. `rmdir` the junctions; never delete recursively through them.
- [ ] **Step 4: Whole-branch review,** then hand over to the owner. Before anything changes in the main checkout, ask the owner to approve these steps. They must be done in this order: the old programs reject `ontario.json`'s new fields, so switching before republishing would make the next scheduled run fail.
  1. Merge `feat/ontario-region` into `main`.
  2. Run `scripts\register-task.ps1` to republish the scheduled programs.
  3. Set `"Region": "ontario"` in the main checkout's `config/sources.json`.
  4. Optionally, move the trial's run folder into `data/raw`, so Ontario starts with one collection.
  5. Run `scripts\run-pipeline.cmd` once. This also applies the migration.
  6. Restart the dashboard.

  **Offer as a follow-up:** count a multi-city posting in every area its location lists.
