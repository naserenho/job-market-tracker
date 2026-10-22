# 0008 — One analysis library, answered from memory

**Status:** Accepted (2026-09-28)

## Context
The report and the dashboard must never disagree about a number. The data is small (a few thousand postings) and changes twice a week.

## Decision
Phase 1's code moves into a `Tracker.Core` library that both the Processor and the API reference. The API loads the analysed postings into memory once (`MarketData`) and answers every filter with plain C# functions, reusing `Analyzer` for the overview.

At most once a minute, a request checks a cheap database version and reloads if it changed. The version is the run count, the latest run and the transaction id of the runs rows, so even a rules-only rebuild counts as a change.

**Consistency.** A load reads everything in one REPEATABLE READ transaction. That transaction first takes ACCESS SHARE locks on the rebuilt tables, in the same order as the rebuild's TRUNCATE. As a result:
- a load never mixes data from before and after a rebuild;
- a load can't deadlock with a rebuild.

**Database down.** The API keeps serving the last load and marks it stale.

**Alternatives considered:**
- **SQL per endpoint:** repeats the analysis rules in SQL, and they drift from the report.
- **A static JSON export:** isn't live, and needs an export step after every change.

## Consequences
- Every filter is a function that can be unit-tested without a database. The endpoint tests compare the API with `Analyzer` directly.
- Memory grows with history. That's fine at tens of thousands of postings; heavy queries can move into SQL later if needed.
- Editing `config/skills.json` or `config/profile.json` needs a dashboard restart, because a database reload doesn't reread them.
