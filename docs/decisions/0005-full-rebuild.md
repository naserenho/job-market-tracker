# 0005 — Rebuild the database from the archive on every run

**Status:** Accepted (2026-09-28)

## Context
Interpretation rules (scope, salary parsing, the skill list) will keep improving. Every improvement should apply to all history, not only to new data.

## Decision
Each processor run reads every archived run and replaces the database contents in one transaction (`TRUNCATE`, then `COPY`). Posting ids are stable (`<source>:<source id>`), so history is preserved across rebuilds.

## Consequences
- A rule change takes effect everywhere at the next run, with no data migrations or backfills. This is the payoff of decision 0001.
- Readers never see a half-built database, and a failed rebuild leaves the previous one intact.
- Cost: the whole archive is re-read each run (about 3.5 GB of JSON after six months, roughly a minute). Readers accept `.json.gz`, so old runs can be compressed if this grows.
- Alternative rejected: incremental processing. It's faster per run, but every rule change would need a backfill mechanism.
