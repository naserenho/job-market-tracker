# 0014 — Pasted jobs age out; companies are grouped by one key

**Status:** Accepted (2026-10-02)

## Context
Two rules in the market data gave numbers that drifted from what they claimed to measure.

- **Pasted jobs never aged.** A file in `data/inbox` counted as seen in the latest collection for as long as the file existed. A job pasted in October was still "active now" in March, and every paste stayed in the 30-day window for ever. During an active search the owner pastes several jobs a week, so the window would fill with jobs nobody had seen for months.
- **Company spellings split counts.** Adzuna writes "Acme Inc.", the company's own board writes "Acme". The report, the dashboard's company lists and the company filter compared trimmed names, so one employer appeared as two with half the postings each. The deduplicator already had a rule for this; nothing else used it.

## Decision
- **A pasted job is seen once.** Its sighting is the first collection that started on or after its `Saved: yyyy-MM-dd` date (the run's local date). If every collection is earlier, the latest one. From there it ages out of the window exactly like a collected posting.
- **No readable `Saved` line keeps the old behaviour** (seen in the latest collection) and adds a warning to the report, so a hand-saved file is never dropped silently.
- **One company key.** `CompanyName.Key` is the deduplicator's normalisation: lower case, punctuation dropped, and the words inc, ltd, llc, corp, corporation, limited, co, company and the removed. A name made only of those words keeps its trimmed lower-case form. Every grouping and the `company` filter use it; the name shown is the group's most frequent spelling, then ordinal order.
- **The tracker's own company records are untouched.** They keep their exact-name key: the owner typed those names and can rename them.

**Alternatives considered:**
- **Keep a pasted job live for a fixed 30 days:** it invents a posting lifetime nobody measured, and the job would still be "active" after the employer closed it.
- **Age by the file's timestamp:** it changes when files are copied, restored from a backup or synced.
- **Delete inbox files after a while:** the archive is the source of truth and is never edited by the tools.
- **A lookup table of company aliases:** exact, but it needs upkeep for every new employer.

## Consequences
- A pasted job leaves "active now" and "best matches" after the next collection, and the window after 30 days. The tracker entry is unaffected: it shows "pasted on <date>" and keeps its job text.
- **A paste can be counted beside its own collected copy.** The deduplicator merges two postings of the same job only when one collection saw both. A paste now has one sighting, so a job pasted before a board first lists it, or pasted twice on different days, is counted twice. Under the old rule the paste was always in the latest collection, which hid this. It is rare (a job on a collected board is normally listed there on the day it is pasted) and is left as it is.
- The tracker shows a pasted job's date from its file name (the day it was pasted), not from its sighting, which is the next collection and can be days later.
- The in-window total can go down between two reports with no posting closing: a paste aged out.
- Two different companies whose names differ only by "Inc", "Ltd" or "The" are merged. None exist in the data today.
- Rebuilds stay deterministic: the sighting depends only on the file and the run dates, never on the clock.
