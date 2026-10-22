# 0003 — A .NET console app instead of a quick script

**Status:** Accepted (2026-09-27)

## Context
A Python or PowerShell script could fetch these URLs in 50 lines. But the archiver is the first component of a system that phase 1 grows into an ingestion worker, and the project doubles as a portfolio piece in the target stack.

## Decision
A .NET 10 Generic Host console app with DI, `IHttpClientFactory` and `Microsoft.Extensions.Http.Resilience`.

## Consequences
- Retries, backoff, `Retry-After` handling and timeouts come from a standard library rather than hand-written code.
- The same host and DI setup carries over to the phase 1 worker and the phase 4 Azure Functions deployment.
- More ceremony than a script, which is accepted for the reasons above.
