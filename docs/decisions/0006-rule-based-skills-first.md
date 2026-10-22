# 0006 — A curated skill list before an LLM

**Status:** Accepted (2026-09-28)

## Context
Skill statistics drive the gap analysis. An LLM extracts skills with more context but costs money per posting, is not deterministic, and is hard to test.

## Decision
Phase 1 matches about 180 skills from `config/skills.json` (names, categories and aliases) with word-edge rules, plus case-sensitive patterns for words that are also ordinary English (React, REST, Go, R). Unit tests pin the tricky cases.

## Consequences
- The matching is free and fast, the same input always gives the same result, and it's easy to explain in an interview.
- It misses context ("5 years of Azure" and "Azure is a plus" count the same) and new tools until someone adds them to the list.
- Phase 3 adds LLM extraction for full-text postings and compares it against these rules.
