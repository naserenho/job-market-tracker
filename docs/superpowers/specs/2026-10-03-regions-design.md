# Regions: design

Date: 2026-10-03. Branch: `feat/regions`.

## Goal

Make the tracker work for a job market other than Toronto by editing one region file, so the repository can be
published as a template. Ship two regions: **Toronto** (today's behaviour, unchanged) and **Abu Dhabi**.

This is part 1 of the owner's "publish as a GitHub template" request. Part 2 (README for people, `AGENTS.md` setup guide
for AI assistants, MIT licence, scrubbed docs, fresh one-commit export) has its own design after this part is merged.

## Owner decisions (2026-10-03)

- Regions are configurable (not a Toronto-only template).
- Presets: Toronto and Abu Dhabi.
- Abu Dhabi counts **Abu Dhabi emirate only** as local (the city, its districts and Al Ain). Dubai counts only when the
  job is remote within the UAE.
- One file per region, chosen by name (approach A).
- Hard requirement: **what the owner has today keeps working.**

## Compatibility guarantees (pass/fail before merge)

With `"Region": "toronto"`:

1. A full rebuild of the owner's existing archive gives, for every one of the 6,989 postings, the same role family,
   seniority, scope, local flag, remote flag, city, salary minimum, maximum, period and origin, citizenship flag,
   duplicate link, sighting runs and skills as the baseline saved on 2026-10-03 before any change. Compared id by id.
   Any difference is a defect.
2. The scheduled task, `scripts/run-pipeline.cmd`, `scripts/register-task.ps1`, the published program paths, their
   arguments and exit codes are unchanged.
3. The tracker's tables, migrations, `data/tracker/tracker.json`, documents and generated CVs are untouched.
4. Dashboard links with `where=gta` keep working. `config/profile.json` and `config/cv.json` need no edits.
5. The existing archive needs no edits: a manifest without a region counts as Toronto.

## Out of scope

- Markets whose postings are not in English: skill and title matching is English-only. The docs say so.
- Several regions at once (one dashboard comparing Toronto and Abu Dhabi). One region is active at a time.
- Currency conversion between regions.
- A region per tracker entry. The application tracker is shared and has no region.

## The region file

`config/sources.json` gains `"Region": "<name>"`. The Archiver, the Processor and the API load
`config/regions/<name>.json`. Field names below are the JSON names; every field is required unless marked optional.

| Field | Meaning | Toronto | Abu Dhabi |
|---|---|---|---|
| `Title` | Report and dashboard title | `GTA Job Market` | `Abu Dhabi Job Market` |
| `LocalLabel` | Name of the local area | `GTA` | `Abu Dhabi` |
| `RemoteLabel` | Location shown for in-country remote roles | `Remote (Canada)` | `Remote (UAE)` |
| `CountryCode` | ISO code; structured country fields are normalised to it | `CA` | `AE` |
| `CountryNames` | Structured country values that mean this country | `Canada`, `CA` | `United Arab Emirates`, `UAE`, `AE` |
| `Culture` | Number and date format | `en-CA` | `en-AE` |
| `TimeZone` | IANA zone for dates on the dashboard | `America/Toronto` | `Asia/Dubai` |
| `InCountry.Words` | Words that place a location in the country (case-insensitive, whole words) | today's Canada and province names | `United Arab Emirates`, `UAE`, `U.A.E`, `Emirates`, the seven emirates |
| `InCountry.Codes` | Subdivision codes after `,` or `-` (case-sensitive) | `ON BC AB QC NS MB NB` | none |
| `Foreign.Words` | Words that mark another country (case-insensitive) | today's list (USA, UK, Australia…) | `Saudi Arabia`, `KSA`, `Riyadh`, `Jeddah`, `Qatar`, `Doha`, `Bahrain`, `Manama`, `Oman`, `Muscat`, `Kuwait`, `Egypt`, `Cairo`, `India`, `Pakistan`, `USA`, `United States`, `UK`, `United Kingdom` |
| `Foreign.Codes` | Codes after `,` that mark another country (case-sensitive) | the US state codes | none |
| `Local.Places` | Place name as written → place shown | today's GTA map | Abu Dhabi, Khalifa City, Mohammed Bin Zayed City / MBZ, Masdar City, Yas Island, Saadiyat Island, Al Reem Island, Al Maryah Island, ADGM, Mussafah, Al Raha, Al Ain → `Abu Dhabi` or `Al Ain` |
| `Local.NeedsCountryContext` | Names that count only with in-country context in the same segment | today's list | `Al Ain` |
| `Salary` | See below | | |
| `CitizenshipPatterns` | Regex fragments (case-insensitive, whole words) for "citizens only or clearance" | today's fragments | `UAE nationals? only`, `UAE national`, `Emirati(?:s)? only`, `Emiratisation`, `Emiratization`, `security clearance` |
| `Adzuna` (optional) | `Country`, `Where`, `DistanceKm`; absent = Adzuna is skipped | `ca`, `Toronto`, 50 | absent |
| `Boards` | Greenhouse, Lever and Ashby boards | today's 39 | verified UAE boards (see below) |
| `CompanyNames` | Board slug → display name | today's map | for its boards |
| `JobBank` (optional) | `DatasetId`, `BackfillMonths`, `EconomicRegion`, `NocCodes`; absent = Job Bank is skipped | today's values | absent |

`Salary`:

| Field | Meaning | Toronto | Abu Dhabi |
|---|---|---|---|
| `Currency` | Code kept for structured salaries and shown in labels | `CAD` | `AED` |
| `Signs` | Home currency marks before an amount | `CA$`, `C$`, `CAD` | `AED`, `Dhs`, `Dh` |
| `SuffixSigns` | Home currency marks after an amount | `CAD`, `CA` | `AED`, `Dhs` |
| `ForeignSigns` / `ForeignSuffixSigns` | Marks that rule a range out | `US$`, `USD` / `USD`, `US` | `US$`, `USD`, `SAR`, `QAR` / `USD`, `SAR`, `QAR` |
| `BareDollar` | What a plain `$` means: `ambiguous` (decided by context) or `foreign` | `ambiguous` | `foreign` |
| `ContextWords` / `ForeignContextWords` | Words near a range that confirm or rule it out | today's Canada / US lists | UAE and emirate names / Gulf country and city names |
| `ForeignContextCodes` | Case-sensitive codes that rule a range out | `US`, `SF` | `KSA` |
| `MonthlyIsSalary` | A per-month range is a salary (period `Month`), not an allowance | `false` | `true` |
| `YearlyMin` / `YearlyMax` | Plausible yearly range in the home currency | 30,000 / 500,000 | 60,000 / 3,000,000 |

For Toronto every list is copied verbatim from the current code and config, in the same order, so the generated
patterns behave exactly as the current hand-written ones (guarantee 1).

Non-regional settings stay where they are: Adzuna search terms, paging, budget and delay in `config/sources.json`;
`WindowDays` in `config/processor.json`; skills, profile and CV. The `Gta`, `CompanyNames` and `JobBank` sections leave
`config/processor.json` and the `Boards` and `JobBank` sections leave `config/sources.json`.

## Behaviour

### Collector (Archiver)

- Loads the region. Adzuna runs only when the region has an `Adzuna` section; otherwise the manifest records it as
  skipped ("not available for this region"). Credentials are needed only when it runs.
- Job Bank runs only when the region has a `JobBank` section.
- Boards come from the region.
- Every manifest records `"region": "<name>"`.

### Interpretation (Tracker.Core)

- `ScopeRules` builds its place, in-country and foreign patterns from the region. `Scope` becomes
  `(City, IsLocal, IsRemoteInCountry)`.
- Structured country values in `CountryNames` are normalised to `CountryCode`; every comparison with `"CA"` uses
  `CountryCode`.
- `SalaryParser` builds its range pattern and context patterns from `Salary`. `SalaryPeriod` gains `Month`
  (yearly = 12 × monthly). When `MonthlyIsSalary` is false a per-month range stays a non-salary amount, as today.
- `SalaryRange.IsPlausible` uses the region's `YearlyMin` and `YearlyMax`.
- Lever and Ashby structured salaries are kept when their currency is `Salary.Currency`.
- The citizenship flag uses `CitizenshipPatterns`.
- Job Bank files are read only when the region has `JobBank`.

### Keeping regions apart

- `Archive.Runs` returns each run's region; a manifest without one counts as `toronto` (the only region before this
  change).
- A rebuild reads only the current region's runs. When it skips any, the report's data warnings say how many and of
  which regions.
- The tracker writes `Region: <name>` into pasted inbox files. An inbox file with another region is skipped (counted in
  one warning); a file without the line belongs to the current region.
- The rebuild's other inputs (skills, profile) are region-independent.

### Database

- A TrackerDb migration renames `postings.is_gta` → `is_local` and `postings.is_remote_canada` →
  `is_remote_in_country`. The Processor applies it on its next run, including `--report-only`. The market tables are
  rebuilt from the archive on every full run, so nothing is lost.
- `ApplicationsDb` is not touched.

### Report, API and dashboard

- The report takes its title, labels, culture and currency text from the region; its methodology lines name the
  region's sources (no Job Bank line without Job Bank).
- `/api/meta` gains `region`: `{ name, title, localLabel, remoteLabel, currency, culture, timeZone }`.
- The `where` filter accepts `all`, `local` and `remote`; `gta` is accepted as `local`.
- The dashboard takes its header, filter labels, number and date formats, time zone, salary currency text and paste
  hint from `/api/meta`.
- `AnalysisPosting` location falls back to `LocalLabel` and `RemoteLabel` instead of "GTA" and "Remote (Canada)".

### Abu Dhabi boards

While building, the executor calls the public Greenhouse, Lever and Ashby board endpoints (no keys, no quota) for
candidate companies with offices in the UAE and keeps only boards that answer and list at least one UAE job at that
time. Adzuna is never called. If fewer than five boards qualify, the preset's file says so in a comment and the docs
name pasting as the main source for this region.

## Errors

- Missing `Region` in `config/sources.json`, an unknown region name (the message lists the files in
  `config/regions/`), unreadable JSON or a missing required field: config error, exit code 2, naming the file and
  field.
- Invalid regex in `CitizenshipPatterns`: config error naming the pattern.
- `YearlyMin` ≥ `YearlyMax`, an empty `Local.Places` or an empty `Boards`: config error.

## Testing

- Both shipped region files load and validate.
- Toronto: every existing scope, salary, citizenship, reader and rebuild test passes unchanged against the Toronto file.
- Abu Dhabi scope: "Abu Dhabi, United Arab Emirates", "Al Ain, Abu Dhabi" and "Yas Island" are local; "Dubai, UAE" is
  not local and not remote; "Remote - UAE" is remote in country; "Riyadh, Saudi Arabia" is out of scope; "Al Ain"
  alone with a foreign marker is not local.
- Abu Dhabi salary: "AED 25,000 - 35,000 per month" is a monthly salary; "25,000 – 35,000 AED monthly" too; a USD
  range on a UAE posting is refused; a plain "$" range is refused; a monthly allowance stays refused.
- Abu Dhabi citizenship: "UAE Nationals only" and "Emiratisation role" are flagged.
- Rebuild: an archive with Toronto and Abu Dhabi runs reads only the current region's runs and warns about the rest;
  a run without a region is Toronto.
- Inbox: a file with another region is skipped with a warning.
- API: `/api/meta` carries the region; `where=gta` equals `where=local`.
- Dashboard: header, filter labels and salary text come from the meta region.
- Archiver: a region without `Adzuna` skips it without credentials; manifests record the region.
- Compatibility (guarantee 1): before asking to merge, the branch's Processor rebuilds the owner's archive into a
  scratch copy of the database (never the live one) and every posting is compared with the 2026-10-03 baseline.
- One final review by a fresh reviewer on the most capable model, then one fix pass.
