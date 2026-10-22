# 0016 — A province-wide local area (Ontario)

**Status:** Accepted (2026-10-20)

## Context
Choosing where in Ontario to live means comparing the GTA, Ottawa, Kitchener-Waterloo and London. The tracker saw
only the GTA plus remote roles open to Canada, so it couldn't compare those cities. ADR 0015 made a market one region
file, but its local area is a list of places, and no list can name every town in a province. Job Bank was filtered to
one economic region, and Adzuna searched 50 km around one city.

## Decision
- **Optional fields only.** Every new behaviour is an optional region-file field; leaving it out keeps today's. The
  Toronto and Abu Dhabi files are unchanged, and a full rebuild of the real archive with `toronto` gives the same result
  for every posting as before the change.
- **`Local.Anywhere`:** words and codes (`ontario`, `ON`) that make any location segment local after the place list
  finds nothing. The city shown is the first part that names a town ("Sudbury, ON" → Sudbury; Adzuna's
  "Canada, Ontario, Timmins" → Timmins), else the local label ("Ontario, Canada" → Ontario). Codes match
  case-sensitively, so "Hybrid - on site" is not "- ON".
- **`Local.Exclude`:** patterns that rule a segment out of `Anywhere` unless the posting's structured country is the
  region's. Ontario lists the US cities: Ontario, California, Oregon, Ohio and New York, and Ontario County, NY.
  "Ontario, CA" means California unless the posting says Canada; after a town ("Timmins, Ontario, CA") it means
  Canada. A segment that names another country ("Ontario, USA") is never local this way either.
- **`Local.Areas`:** the place shown → an area, for a new report section, "Where the jobs are". It gives postings in
  the window per area, with share and the median posted salary when at least 10 postings give one. Every area appears
  even with none. Job Bank's tech postings per economic region come with it. Regions without `Areas` (Toronto) don't
  get the section.
  - **Postings that name no town** ("Ontario, Canada") get their own row, "Ontario, no city given", so they don't
    inflate Other Ontario's small towns.
  - **Remote roles are their own row,** including remote roles limited to the province ("Remote - Ontario").
- **`JobBank.Province`:** filters Job Bank rows on `Province/Territory` instead of `Economic Region` (exactly one of the
  two). Each rebuild also stores tech postings per economic region and month in a new table, `jobbank_regions`.
- **`Adzuna.DistanceKm` is optional.** Left out, no `distance` is sent. A dry run on 2026-10-20 with `Where: "Ontario"`
  returned 275 results for ".net developer", all in Ontario.
- **Two more documented public job feeds,** additions to ADR 0002's list. Each was kept because its endpoint answered
  without a key and at least 3 Ontario companies hiring in Ottawa, Kitchener-Waterloo or London were on it (probed
  2026-10-20):
  - **Workable:** `apply.workable.com/api/v1/widget/accounts/<slug>?details=true`, Workable's documented careers-page
    feed. March Networks, Fidus Systems and 49 Solutions.
  - **SmartRecruiters:** `api.smartrecruiters.com/v1/companies/<id>/postings`, asked for the region's country
    (`country=ca`) and paged up to 10 × 100. Its list has no text, so each in-country posting's detail is saved in a
    folder beside the list file; the list pages are saved combined as one list file, each page still in the manifest.
    Assent, ADGA, General Dynamics Mission Systems, Kuatro and SA Technologies.
  - **Recruitee was excluded:** its documentation says calls without its token get `401 Unauthorized`, so it isn't a
    public feed.

**Alternatives considered:**
- **A region per city** (Ottawa, Waterloo, London): rejected by the owner. Comparing cities would mean switching
  regions and rebuilding, and each would split the 80-call Adzuna budget.
- **A complete Ontario place list with no fallback:** long, never complete, and every unlisted town silently out of
  scope.
- **Adzuna searches per city:** better small-city coverage, but they split the 80-call budget. This is the follow-up if
  a month of data shows Adzuna's Ontario results are almost all Toronto.

## Consequences
- **Adzuna leans towards Toronto:** at most 200 results per term, most in the GTA. Company boards and Job Bank carry
  the smaller cities.
- **Multi-city postings count once,** under the first city listed. That undercounts the smaller cities a little.
- **Ontario starts without Toronto's history:** regions never mix (ADR 0015). The first Ontario rebuild has Job Bank's
  12 months and one collection; the 30-day window fills over about 4 weeks.
- **An upgrade adds a table.** Until the Processor applies the migration, the dashboard says the market data needs an
  update (an older schema missing a table now counts as outdated, not as "never rebuilt").
