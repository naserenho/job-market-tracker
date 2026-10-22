import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render } from '@testing-library/react';
import type { ReactElement } from 'react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router';
import { vi } from 'vitest';
import type { Meta } from '../api';

export const META: Meta = {
  latestRunId: '2026-10-01_0700',
  latestRunAt: '2026-10-01T11:00:00Z',
  runs: 3,
  historyDays: 30,
  targetFamilies: ['Dotnet', 'Fullstack', 'SoftwareEngineer', 'LeadManager'],
  families: ['Dotnet', 'Fullstack', 'SoftwareEngineer', 'LeadManager', 'Other'],
  seniorities: ['Unspecified', 'Junior', 'Intermediate', 'Senior', 'StaffPrincipal', 'Lead', 'Manager'],
  categories: ['backend', 'devops', 'language'],
  companies: ['Acme', 'Globex'],
  stale: false,
  region: {
    name: 'toronto', title: 'GTA Job Market', localLabel: 'GTA', remoteLabel: 'Remote (Canada)', currency: 'CAD', moneyPrefix: '$',
    culture: 'en-CA', timeZone: 'America/Toronto',
  },
};

export const ABU_DHABI_META: Meta = {
  ...META,
  region: {
    name: 'abu-dhabi', title: 'Abu Dhabi Job Market', localLabel: 'Abu Dhabi', remoteLabel: 'Remote (UAE)', currency: 'AED', moneyPrefix: 'AED ',
    culture: 'en-AE', timeZone: 'Asia/Dubai',
  },
};

export function LocationProbe() {
  const location = useLocation();
  return <output data-testid="location">{location.pathname + location.search}</output>;
}

// Renders `element` at `url` inside a router and a fresh query client (no retries), plus a probe showing the current URL
// (outside the route, so it stays visible after navigating away). Returns the query client too.
export function renderRoute(url: string, element: ReactElement, path = '*') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const view = render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[url]}>
        <Routes>
          <Route path={path} element={element} />
        </Routes>
        <LocationProbe />
      </MemoryRouter>
    </QueryClientProvider>,
  );
  return { ...view, client };
}

const REPLY = Symbol('reply');

// A canned answer with a status code (and body, if any).
export function reply(status: number, body?: unknown) {
  return { [REPLY]: true, status, body };
}

// A problem-details error, with extra members such as field or id.
export function failure(status: number, detail: string, extra: Record<string, unknown> = {}) {
  return reply(status, { title: 'Error', status, detail, ...extra });
}

// Replaces fetch.
// - A route key is a path prefix for GET ("/api/meta") or "METHOD /prefix" for other methods
//   ("POST /api/tracker/applications"). The longest matching key wins.
// - A function value is called with the request init.
// Returns the requests made: GET as the URL, others as "METHOD URL".
export function mockApi(routes: Record<string, unknown>): string[] {
  const calls: string[] = [];
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input);
    const method = (init?.method ?? 'GET').toUpperCase();
    const id = method === 'GET' ? url : `${method} ${url}`;
    calls.push(id);
    const key = Object.keys(routes).filter((k) => id.startsWith(k)).sort((a, b) => b.length - a.length)[0];
    const route = key === undefined ? failure(500, `No mock for ${id}`) : routes[key];
    const value = typeof route === 'function' ? (route as (init?: RequestInit) => unknown)(init) : route;
    const canned = typeof value === 'object' && value !== null && REPLY in value;
    const { status, body } = canned ? (value as unknown as { status: number; body: unknown }) : { status: 200, body: value };
    if (status === 204) return new Response(null, { status });
    return new Response(JSON.stringify(body ?? null), { status, headers: { 'Content-Type': 'application/json' } });
  }));
  return calls;
}

// The body sent with the latest request whose "METHOD URL" (or GET URL) starts with `prefix`; JSON is parsed.
export function sentBody(prefix: string): unknown {
  const calls = vi.mocked(fetch).mock.calls;
  for (let i = calls.length - 1; i >= 0; i--) {
    const [input, init] = calls[i];
    const method = (init?.method ?? 'GET').toUpperCase();
    const id = method === 'GET' ? String(input) : `${method} ${String(input)}`;
    if (id.startsWith(prefix)) return typeof init?.body === 'string' ? JSON.parse(init.body) : init?.body;
  }
  return undefined;
}
