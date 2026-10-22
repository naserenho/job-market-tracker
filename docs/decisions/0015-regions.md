# 0015 — One file per region, chosen by name

**Status:** Accepted (2026-10-03)

## Context
The tracker was built for one market: Toronto and the GTA, plus remote roles open to Canada. That market was spread
through the code:
- the GTA place list, Canada and province words, foreign-country markers and US state codes in `ScopeRules`;
- CAD, US-dollar and Canadian place words in `SalaryParser`;
- "Canadian citizenship" in the citizenship flag;
- Canada's Adzuna endpoint, the Toronto job boards and the Toronto Job Bank region in config;
- en-CA formats, the Toronto time zone and "GTA" labels in the report and dashboard;
- `is_gta` and `is_remote_canada` in the database.

Publishing it as a template, and using it for Abu Dhabi, needed all of that to move out of the code without changing
a single Toronto result.

## Decision
- **One file per region:** `config/regions/<name>.json` holds everything that differs between markets. That's its
  names and labels, country code and names, number and date culture, time zone, local places, in-country and foreign
  markers, salary rules (currency signs, context words, whether monthly pay is salary, plausible yearly range),
  citizenship phrases, Adzuna (optional), job boards, company names and Job Bank (optional).
- **Chosen by name:** `"Region"` in `config/sources.json` names the active file. The Archiver layers it over its
  settings; the Processor and API load it into a `Region` object (the API also accepts `Tracker:Region`).
- **Word lists are regular-expression fragments:** the code builds the same patterns the hand-written rules used.
  Tests pin that the Toronto file reproduces them character for character, and a full rebuild of the real archive
  gave identical results for all 6,989 postings.
- **Regions never mix:** every manifest and every pasted file records its region. A rebuild reads only the active
  region's data and says what it skipped. Collections from before this change have no region and count as Toronto.
- **Neutral column names:** `is_gta` and `is_remote_canada` become `is_local` and `is_remote_in_country`. The
  migration renames rather than drops, so `--report-only` right after the upgrade still has every value.
- **Shipped regions:** Toronto (unchanged) and Abu Dhabi. Adzuna has no UAE market and Job Bank is Canadian, so Abu
  Dhabi collects public job boards and pasted postings, reads monthly AED pay, and flags "UAE nationals only" roles.

**Alternatives considered:**
- **Preset folders to copy over `config/`:** no selection code, but every non-regional setting is duplicated per
  region, and a half-done copy mixes two regions.
- **A C# class per region:** precise for tricky rules, but adding a city would mean writing and compiling code: wrong
  for a template.
- **Plain word lists with no regular expressions:** friendlier to edit, but they can't express rules the Toronto
  version already relied on (`U\.S\.A?\.?`, `citizens? of canada`), so Toronto results would change.

## Consequences
- A region file is partly regular expressions; its header comments and the README explain the few rules that need
  escaping.
- Matching skills and titles is English-only, so regions make sense for English-language job markets.
- One region is active at a time. Switching keeps both regions' data in the same `data/` folder; each rebuild reads its
  own. The API and dashboard need a restart after a switch.
- The application tracker is shared by every region; entries have no region.
- Adding a market is a JSON file plus a test run, not a code change.
