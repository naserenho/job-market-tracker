# 0002 — Official APIs and open data only; no scraping

**Status:** Accepted (2026-09-27)

## Context
LinkedIn and Indeed carry most Toronto tech postings. Both forbid scraping in their terms and actively block bots. Most bank careers sites run on Workday, whose JSON endpoints are undocumented.

## Decision
Collect only from documented, public sources:
- the Adzuna API (personal research is permitted; Adzuna must be credited as the source)
- public Greenhouse, Lever and Ashby job-board APIs
- Job Bank open data (Government of Canada)

Postings from LinkedIn, Indeed and bank sites enter through `data/inbox/`, pasted by hand.

## Consequences
- No legal or account risk, and the sources don't break when a site changes its HTML.
- Adzuna gives breadth but only description snippets. Boards and the inbox give full text. Phase 1 must track which source each statistic comes from.
- Coverage of bank roles depends on Adzuna and manual pasting.
