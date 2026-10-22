# Ontario region: design

Date: 2026-10-19. Branch, once approved: `feat/ontario-region`. Status: **draft, awaiting the owner's approval.**

## Goal

Add a region, `ontario`, whose local area is the whole province. The owner can then compare the job markets of the
cities they might live in from one report: the GTA, Ottawa, Kitchener-Waterloo, London and the rest of Ontario.

**Why:** the owner is choosing where in Ontario to live. Today the tracker
only sees the GTA, plus remote roles open to Canada, so it can't say whether Ottawa, Kitchener-Waterloo or London have
the work. Job Bank's figures by economic region were the stand-in, and they carry few tech roles.

**Success:** after a month of Tuesday/Friday runs, the report shows how many in-scope postings each area has and what
they pay, so the choice of city can weigh where the work is.

## Owner decisions (2026-10-19)

- **One Ontario region** covering the whole province, not a region per city.
- **Carried over from the regions design:** what the owner has today keeps working.
- **2026-10-20:** add public job feeds from more board services, to reach smaller-city companies. There's no scraping,
  and the scheduled run doesn't use a browser.

## Compatibility guarantees (pass/fail before merge)

1. **Toronto rebuilds identically.** With `"Region": "toronto"`, a full rebuild of the existing archive gives the same
   results for every posting as a baseline saved before any change. The fields compared are the regions design's list,
   id by id. The report HTML is also unchanged, apart from its timestamp.
2. **The shipped region files are untouched.** `toronto.json` and `abu-dhabi.json` don't change. Every new region-file
   field is optional, and leaving it out keeps today's behaviour.
3. **Nothing else changes.** The scheduled task, scripts, program paths, arguments and exit codes stay the same. So do
   the tracker tables, `tracker.json`, documents and CVs.
4. **The existing archive needs no edits.**

## What the Ontario region counts

### Local: anywhere in Ontario

Today a posting is local only if its location names a place in `Local.Places`. No list can name every town in Ontario,
so:

- **New optional `Local.Anywhere`** (`Words` and `Codes`, like `InCountry`): a location segment that matches one and
  isn't marked as another country counts as local. For Ontario: the word `Ontario`, and the code `ON` after a comma or
  dash.
- **The city shown:**
  - a known place from `Local.Places` when the segment names one (`Kanata, ON` → Ottawa);
  - otherwise the segment's first part (`Sudbury, ON` → Sudbury);
  - `Ontario` when the segment names no city (`Ontario, Canada`).
- **Ontario, California:** `Ontario, CA` and `Ontario, California` are the US city. They count as foreign, not local,
  unless the posting's structured country is Canada.
- **Ambiguous city names:** names shared with other countries need in-country context in the same segment
  (`NeedsCountryContext`). Examples: London, Cambridge, Waterloo, Hamilton, Kingston, Windsor, Ottawa, Richmond Hill,
  Peterborough.
- **Remote** is unchanged: remote roles open to Canada, shown as `Remote (Canada)`.

### Areas, for comparing cities

**New optional `Local.Areas`:** the place shown → area.

- **What it's keyed on:** the place shown, not the raw location text. That's a value in `Local.Places`, or the first
  segment for a town that isn't listed. So `Kanata` and `Nepean` don't need entries: `Local.Places` already shows them
  as Ottawa.
- **What it's for:** postings are grouped into areas for the new report section (below). Places not listed fall into
  `Other Ontario`.

My call on the grouping:

| Area | Places shown |
|---|---|
| Toronto area (GTA) | The places `toronto.json` shows: Toronto, the Peel, York, Durham and Halton municipalities, and the region names |
| Hamilton | Hamilton |
| Kitchener-Waterloo | Kitchener, Waterloo, Cambridge, Guelph |
| London | London |
| Ottawa | Ottawa (Kanata, Nepean and Orléans are shown as Ottawa) |
| Other Ontario | Everything else in Ontario |

### Sources

**Adzuna:**

- One local search with `Where: "Ontario"`. The terms and the 80-call budget are unchanged, so the call plan is the
  same 70 calls as Toronto's.
- **`DistanceKm` becomes optional.** When it's absent, the `distance` parameter is left out.
- **The builder checks first,** with one `--dry-run`: does Adzuna read `Ontario` as the province, and does `distance`
  need to be dropped?
- **Expected weakness:** each term returns at most 200 results (4 pages × 50). Most will be in Toronto, so Ottawa,
  Kitchener-Waterloo and London will be thin in Adzuna. Job Bank and the company boards fill in.

**Job Bank:**

- **New optional `Province`** in the `JobBank` section. When set, rows match on the `Province/Territory` column (the
  files hold full names, e.g. `Ontario`) instead of `Economic Region`. A region file must set exactly one of the two.
- **Each Job Bank row keeps its economic region,** so the report can show Job Bank postings per region. The files
  write some names with an en dash, e.g. `Kitchener–Waterloo–Barrie Region`. This adds a column to the Job Bank table;
  the market tables are rebuilt on every full run, so nothing is lost.
- **No new downloads:** the monthly files already on disk are national, so the Ontario rebuild has 12 months of Job
  Bank data from its first run.

**Company boards:**

- Start from Toronto's 39 boards.
- Add companies that hire in Ottawa, Kitchener-Waterloo and London. Keep only boards that `--verify-boards` confirms
  and that list Ontario locations. The builder finds the candidates; none are listed here from memory.

**Two new board types (owner decision, 2026-10-20).** The aim is more Ottawa, Kitchener-Waterloo and London companies
without scraping. That keeps ADR 0002: documented, public endpoints only.

| ATS | Endpoint | What its documentation says (checked 2026-10-20) |
|---|---|---|
| `workable` | `https://www.workable.com/api/accounts/{slug}?details=true` (jobs with descriptions); `/locations` and `/departments` also exist | Workable's help centre lists these as "public endpoints" for building a careers page, with no authorization header ([Workable help](https://help.workable.com/hc/en-us/articles/115012771647)) |
| `smartrecruiters` | `https://api.smartrecruiters.com/v1/companies/{slug}/postings` (list); `.../postings/{id}` (one posting in full) | The endpoint reference shows the list call with no auth header ([endpoints](https://developers.smartrecruiters.com/docs/endpoints)). The Posting API overview mentions API keys and OAuth ([Posting API](https://developers.smartrecruiters.com/docs/posting-api)). The two pages disagree |

- **The builder checks each type before writing code for it.** Its documented endpoint must answer without a key,
  and at least 3 Ontario tech companies hiring in Ottawa, Kitchener-Waterloo or London must be on it. A type that fails
  either check is dropped and recorded in ADR 0016. No code gets written for a source nobody uses.
- **Each type needs:**
  - a URL in `BoardUrls`;
  - a reader in `SourceReaders` that turns its JSON into `RawPosting`s, using the same fields as the other boards;
  - recognition of the new `Ats` value when region files are checked.
- **SmartRecruiters full text:** the list call returns no descriptions. The collector fetches a posting's full text
  only when the list's location is in the country (local or remote), so the extra requests stay few.
- **Structured salaries** are kept only when the currency is the region's, as for Lever and Ashby.
- **Recruitee is excluded.** Its own documentation says calls without its token get `401 Unauthorized`, so it isn't
  public ([Recruitee docs](https://docs.recruitee.com/reference/authentication-1.md)).

### The rest of the region file

- **Labels:** `Title` `Ontario Job Market`, `LocalLabel` `Ontario`, `RemoteLabel` `Remote (Canada)`.
- **Salary:** as Toronto, with Ontario city names added to `ContextWords`: ottawa, waterloo, kitchener, london ontario,
  hamilton.
- **CitizenshipPatterns:** as Toronto. Ottawa has many government and defence roles that require clearance, and the
  existing flag already marks them.

## Report

**New section, "Where the jobs are":** shown only when the region has `Local.Areas`. Toronto has none, so its report
is unchanged (guarantee 1).

- **Postings in the window, by area,** with remote roles as their own row. For each row:
  - the count, and its share of in-scope postings;
  - the median posted salary, when at least 10 postings in the area give one.
- **Job Bank postings over 12 months, by economic region.**
- **One line saying where the numbers come from.** Adzuna results lean towards Toronto (above), so the boards and Job
  Bank carry the smaller cities.

The dashboard needs no changes in this round. Its labels already come from the region.

## What the owner sees after switching

- **Switching:** set `"Region": "ontario"` in `config/sources.json`, run `scripts\run-pipeline.cmd`, then restart the
  dashboard.
- **Ontario starts without Toronto's history.** Regions never mix (ADR 0015), so the first Ontario rebuild has:
  - Job Bank's 12 months;
  - one run of Adzuna and boards.
- **The 30-day window fills** over about 4 weeks of Tuesday/Friday runs.
- **Jobs pasted under Toronto** don't count in Ontario.
- **Switching back to `toronto`** still works, and keeps its own history.

## Tests

- **`RegionTests`** load `ontario.json`.
- **`ScopeRules`, Ontario cases:**

  | Location | Expected |
  |---|---|
  | `Ottawa, ON` | Local, Ottawa |
  | `Waterloo, Ontario, Canada` | Local, Waterloo |
  | `Sudbury, ON` | Local, Sudbury, area Other Ontario |
  | `Ontario, Canada` | Local, city Ontario |
  | `London, ON` | Local, London |
  | `London, UK` | Not local |
  | `Waterloo, IA` | Not local |
  | `Ontario, CA` with no structured country | Not local |
  | `Remote - Canada` | Remote |

- **Toronto:** the existing pinned patterns are unchanged, and a full-archive comparison against the baseline is part
  of the merge check.
- **Job Bank:**
  - `Province` picks Ontario rows from every economic region;
  - the `EconomicRegion` path is unchanged;
  - a file with both fields set, or neither, is rejected.
- **Adzuna:** the URL leaves out `distance` when `DistanceKm` is absent; Toronto's URLs are unchanged.
- **New board types:** each reader is tested against a saved sample response:
  - title, company, URL, location, remote flag, full text and salary;
  - an unknown `Ats` value is still rejected.
- **Report:** the area section renders for a region with `Areas` and is absent for Toronto.

## Docs

- **README:**
  - Regions: add the `ontario` row and the new optional fields;
  - the Phase 0 source table: add the board types that were kept.
- **ADR 0016, "A province-wide local area":** the `Anywhere` matcher, optional fields only, the Job Bank province
  filter and the area section. It also records which new board types passed the checks, as additions to ADR 0002's
  list of documented public sources, and which were dropped, and why.

## Out of scope

- **Separate Adzuna searches per city.** They would split the 80-call budget. This is the follow-up if a month of data
  shows Adzuna's Ontario results are almost all Toronto.
- **An area filter or area page on the dashboard.**
- **Any change to the Toronto or Abu Dhabi regions.** They can use the new board types later by adding boards to their
  files.
- **Scraping, and browser automation in the scheduled run.** This covers LinkedIn, Indeed, Workday career sites and
  Recruitee (ADR 0002). Postings from sites without an API still come in through `data/inbox/`. When the owner asks,
  Claude can read public career pages in the owner's browser and save the postings there. That's by hand, not part of
  this build.

## Left to the builder

- **The full `Local.Places` list** for Ontario municipalities, and which names need country context.
- **Which boards to add,** after `--verify-boards`, on any of the five board types.
- **Whether Workable and SmartRecruiters pass the checks** above.
- **Adzuna:** whether `distance` is needed for `Where: "Ontario"`, from the dry run.
- **Whether to make `ontario` the active region** at the end. The owner's request implies yes; confirm when handing
  over.
