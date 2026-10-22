import { screen, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { App } from './App';
import { ABU_DHABI_META, META, failure, mockApi, renderRoute } from './test/render';
import { DEFAULT_DISPLAY, setDisplayRegion } from './format';

describe('App', () => {
  it('says when there is no data yet', async () => {
    mockApi({ '/api/meta': { ...META, runs: 0, latestRunId: null, latestRunAt: null } });
    renderRoute('/', <App />);
    expect(await screen.findByText(/No data yet/)).toBeInTheDocument();
  });

  it("shows the API's message when the database is down on a cold start", async () => {
    mockApi({ '/api/meta': failure(503, "The tracker database isn't running. Start Docker Desktop (and `docker compose up -d postgres`).") });
    renderRoute('/', <App />);
    expect(await screen.findByRole('alert')).toHaveTextContent('docker compose up -d postgres');
  });

  it('warns when the numbers come from before a database outage', async () => {
    mockApi({ '/api/meta': { ...META, stale: true, runs: 0 } });
    renderRoute('/', <App />);
    expect(await screen.findByText(/last successful load/)).toBeInTheDocument();
  });

  it('refetches the page data when a new run lands, so the numbers match the run in the header', async () => {
    let runId = '2026-10-01_0700';
    const calls = mockApi({ '/api/meta': () => ({ ...META, latestRunId: runId }), '/api/overview': { report: null } });
    const overviewCalls = () => calls.filter((u) => u.startsWith('/api/overview')).length;
    const { client } = renderRoute('/', <App />);
    await waitFor(() => expect(overviewCalls()).toBe(1));

    runId = '2026-10-02_0700';
    await client.invalidateQueries({ queryKey: ['meta'] });

    expect(await screen.findByText(/Run 2026-10-02_0700/)).toBeInTheDocument();
    await waitFor(() => expect(overviewCalls()).toBe(2));
  });

  it('keeps the filters in the page links', async () => {
    mockApi({ '/api/meta': { ...META, runs: 0 } });
    renderRoute('/?where=gta', <App />);
    expect(await screen.findByRole('link', { name: 'Skills' })).toHaveAttribute('href', '/skills?where=local');
  });

  it('shows the overdue count on the Tracker link and hides the market filters there', async () => {
    mockApi({
      '/api/meta': META,
      '/api/tracker/status': { migrated: true, lastBackupAt: null, lastBackupError: null, counts: {}, overdue: 2 },
      '/api/tracker/up-next': [],
      '/api/tracker/applications': [],
    });
    renderRoute('/tracker', <App />);

    expect(await screen.findByRole('link', { name: /^Tracker\W+2 overdue$/ })).toHaveAttribute('href', '/tracker');
    expect(await screen.findByText(/Nothing tracked yet/)).toBeInTheDocument();
    expect(screen.queryByText('Roles')).not.toBeInTheDocument();
  });

  it('warns about a failed backup on every tracker page', async () => {
    mockApi({
      '/api/meta': META,
      '/api/tracker/status': { migrated: true, lastBackupAt: null, lastBackupError: 'The backup copy in data/tracker/tracker.json could not be updated: disk full', counts: {}, overdue: 0 },
      '/api/tracker/companies': [],
    });
    renderRoute('/tracker/new', <App />);

    expect(await screen.findByText(/could not be updated: disk full/)).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Paste a job' })).toBeInTheDocument();
  });

  it('takes its title and filter labels from the region', async () => {
    mockApi({ '/api/meta': { ...ABU_DHABI_META, runs: 0 } });
    renderRoute('/', <App />);

    expect(await screen.findByRole('heading', { level: 1, name: 'Abu Dhabi Job Market' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'Abu Dhabi only' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'Remote (UAE) only' })).toBeInTheDocument();
    setDisplayRegion(DEFAULT_DISPLAY);
  });

  it('shows the Toronto title for the Toronto region', async () => {
    mockApi({ '/api/meta': { ...META, runs: 0 } });
    renderRoute('/', <App />);
    expect(await screen.findByRole('heading', { level: 1, name: 'GTA Job Market' })).toBeInTheDocument();
  });

  // Tracker pages don't wait for /api/meta: before it answers they draw with the default format and a neutral title.
  it('renders the tracker before the region is known', async () => {
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.startsWith('/api/meta')) return new Promise<Response>(() => {});
      const body = url.startsWith('/api/tracker/status')
        ? { migrated: true, lastBackupAt: null, lastBackupError: null, counts: {}, overdue: 0 }
        : [];
      return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } });
    }));
    renderRoute('/tracker', <App />);

    expect(screen.getByRole('heading', { level: 1, name: 'Job market' })).toBeInTheDocument();
    expect(await screen.findByRole('heading', { level: 2, name: 'Tracker' })).toBeInTheDocument();
  });
});
