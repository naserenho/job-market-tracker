# 0001 — Store raw responses, parse later

**Status:** Accepted (2026-09-27)

## Context
Trend analysis needs history, and history only exists if we collect it from now on. The parsing rules (skill taxonomy, title normalization, dedupe) will change many times in phase 1 and after.

## Decision
Phase 0 saves every response byte-for-byte into dated run folders and does no parsing. Phase 1 builds its database by re-processing the whole archive.

## Consequences
- A parsing bug never loses data: fix the parser and re-run it over the archive.
- Collection could start weeks before the processing pipeline existed.
- Costs disk space (Job Bank CSVs are ~50 MB/month, stored gzipped) and duplicate postings across runs. Dedupe happens in phase 1.
- Alternative rejected: parsing on ingest into a database. It's faster to query on day one, but every schema mistake is permanent.
