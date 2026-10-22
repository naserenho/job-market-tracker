import type { ReactNode } from 'react';

export function Loading({ what }: { what: string }) {
  return <p className="empty" role="status">Loading {what}…</p>;
}

export function ErrorBanner({ error }: { error: unknown }) {
  const message = error instanceof Error ? error.message : 'Something went wrong.';
  return <div className="banner" role="alert">{message}</div>;
}

export function StaleBanner() {
  return (
    <div className="banner warn" role="status">
      The latest refresh failed, so these numbers are from the last successful load. If Docker Desktop isn't running,
      start it (and <code>docker compose up -d postgres</code>). Otherwise the API's logs in the Aspire dashboard say
      what went wrong; the next try is within a minute.
    </div>
  );
}

export function Empty({ children }: { children: ReactNode }) {
  return <p className="empty">{children}</p>;
}

// Loading or error for a query that has no data yet.
export function QueryState({ query, what }: { query: { isPending: boolean; error: unknown }; what: string }) {
  if (query.error) return <ErrorBanner error={query.error} />;
  if (query.isPending) return <Loading what={what} />;
  return null;
}
