# 0007 — .NET Aspire to run the dashboard locally

**Status:** Accepted (2026-09-28)

## Context
Phase 2a adds two processes next to the database: an ASP.NET Core API and a Vite dev server. They need each other's addresses and a connection string, and it should be possible to see what happens inside them: requests, SQL and errors. Observability is also a skill the market data asks for.

## Decision
A .NET Aspire 13 AppHost starts the API and the Vite app and runs the Aspire dashboard.
- It passes the API's address to Vite and the connection string to the API.
- The API uses the service-defaults project for OpenTelemetry traces, metrics and logs, and for health checks.
- The AppHost points at the existing Compose Postgres with `AddConnectionString` instead of starting its own container. The scheduled Processor rebuilds that database twice a week, and that has to keep working when the dashboard isn't running.

## Consequences
- One command (`scripts\dashboard.cmd`) starts everything, and a trace shows each request down to its SQL.
- The same service defaults carry over to Azure Container Apps in phase 4.
- Docker Compose remains for the database only, so there are two ways of starting things. The README says which is which.
- On this PC the AppHost runs over plain HTTP (`ASPIRE_ALLOW_UNSECURED_TRANSPORT`), which avoids trusting a dev certificate for a local-only tool.
