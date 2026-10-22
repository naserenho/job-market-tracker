# 0009 — React, TypeScript and Vite for the dashboard

**Status:** Accepted (2026-09-28)

## Context
The dashboard is an interactive client for a JSON API on this PC. React and TypeScript appear in many GTA full-stack postings.

## Decision
React 19 with TypeScript, built by Vite, using:
- TanStack Query for fetching and caching;
- React Router, with every filter kept in the URL;
- Recharts for the charts;
- Vitest with Testing Library for tests.

**Alternatives considered:**
- **Next.js:** server rendering and file routing add nothing to a single-user local tool whose API is already .NET.
- **Blazor:** would keep everything in C#, but React is asked for far more often in postings.

## Consequences
- The types in `api.ts` are written by hand to mirror the C# records. The compiler checks every page against them, but nothing yet checks them against the API: a renamed C# property would break a page at run time, not the build. Generating them from an OpenAPI document would close that gap.
- Filters in the URL make every view bookmarkable, and back/forward work.
- There are two toolchains (dotnet and npm). Aspire starts both; the scheduled task needs neither.
