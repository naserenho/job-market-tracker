# 0010 — The owner's records next to a destructive rebuild

**Status:** Accepted (2026-09-29)

## Context
The market tables are wiped and reloaded from the archive twice a week (ADR 0005). Phase 2b adds data that can't be recollected: the postings the owner shortlists, their applications, timelines, contacts and the files they sent.

## Decision
- **Separate context.** An `ApplicationsDb` context owned by the API holds six tables in the same Postgres database. It has its own migrations history table (`__applications_migrations`), and its tables are never in `TrackerTables.All`, the rebuild's TRUNCATE list. A test fails if they ever are.
- **Stable ids plus copies.** Entries link to market postings by their stable ids (`greenhouse:101`, `inbox:<file>`) and keep their own copy of the title, link and location, so an entry outlives its posting.
- **JSON mirror.** After every committed change, the API rewrites `data/tracker/tracker.json` (a temp file, then a swap).
  - When the tables are found empty and the copy isn't, the API restores them in one transaction.
  - A corrupt copy keeps the tracker closed instead of being overwritten.
- **Pasted jobs.** A pasted job is also written to `data/inbox/`, so the next rebuild counts it as a market posting and the entry links to it automatically.

**Alternatives considered:**
- **JSON files only:** simplest, but no queries, and concurrency by hand.
- **Tables in the Processor's context:** one migration history, but every rebuild change would put the owner's data at risk.

## Consequences
- **The rebuild stays simple and destructive;** the owner's data is structurally out of its reach.
- **Two migration histories in one database.** The API migrates its own tables on the first tracker request.
- **The mirror sits on the same disk.** It covers losing the Docker volume, not losing the disk. Off-site backup can come with the Azure move in phase 4.
