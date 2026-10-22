# 0004 — PostgreSQL for the tracker database

**Status:** Accepted (2026-09-28)

## Context
Phase 1 needs a relational store for postings, skills and sightings, queried by the report now and by the phase 2 API later. The owner knows SQL Server deeply; Toronto startups and scale-ups mostly run PostgreSQL, and banks mostly run SQL Server.

## Decision
PostgreSQL 17 in Docker Compose, accessed with EF Core (Npgsql) with snake_case names. Raw Adzuna aggregates are stored as `jsonb`, and bulk loads use Postgres binary `COPY`.

## Consequences
- A second production database to work with, plus hands-on practice with Postgres-specific features (`jsonb`, `COPY`, `percentile_cont`).
- The database runs in Docker, so the pipeline depends on Docker Desktop at 07:00. The processor exits with code 3 and a clear message when Postgres is unavailable, and collection still happens.
- Alternative rejected: SQL Server. It would be faster for the owner to read, but teaches less that's new.
