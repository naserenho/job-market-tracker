import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { mockApi, renderRoute } from '../test/render';
import type { ApplicationRow, TrackerStatus } from './api';
import { TrackerPage } from './TrackerPage';

const STATUS: TrackerStatus = { migrated: true, lastBackupAt: '2027-03-01T10:00:00Z', lastBackupError: null, counts: { Applied: 1, Interviewing: 1 }, overdue: 1 };
const ROWS: ApplicationRow[] = [
  { id: 'a1', title: 'Senior .NET Developer', company: 'RBC', status: 'Interviewing', next: { id: 'f1', action: 'Prep panel', dueOn: '2027-03-05' },
    updatedAt: '2027-03-01T10:00:00Z', postingId: 'greenhouse:101', live: true },
  { id: 'a2', title: 'Tech Lead', company: 'Shopify', status: 'Applied', next: null, updatedAt: '2027-02-27T10:00:00Z', postingId: null, live: false },
];
const routes = (extra: Record<string, unknown> = {}) => ({
  '/api/tracker/status': STATUS, '/api/tracker/up-next': [], '/api/tracker/applications': ROWS, ...extra,
});

describe('Tracker page', () => {
  it('lists entries with their status, next action and a link to each', async () => {
    mockApi(routes());
    renderRoute('/tracker', <TrackerPage />);

    const link = await screen.findByRole('link', { name: 'Senior .NET Developer' });

    expect(link).toHaveAttribute('href', '/tracker/a1');
    const row = link.closest('tr') as HTMLElement;
    expect(within(row).getByText('Interviewing')).toBeInTheDocument();
    expect(within(row).getByText(/Prep panel/)).toBeInTheDocument();
    expect(within(row).getByText(/Live/)).toBeInTheDocument();
    expect(screen.getByText('No posting')).toBeInTheDocument();
  });

  it('keeps status, search and sort in the URL and asks the API for them', async () => {
    const calls = mockApi(routes());
    renderRoute('/tracker', <TrackerPage />);
    await screen.findByRole('link', { name: 'Tech Lead' });

    fireEvent.change(screen.getByLabelText('Status'), { target: { value: 'Applied' } });
    fireEvent.change(screen.getByLabelText('Sort'), { target: { value: 'company' } });
    fireEvent.change(screen.getByLabelText('Role or company'), { target: { value: 'shop' } });
    fireEvent.click(screen.getByRole('button', { name: 'Search' }));

    await waitFor(() => expect(calls).toContain('/api/tracker/applications?status=Applied&q=shop&sort=company'));
    expect(screen.getByTestId('location').textContent).toBe('/tracker?status=Applied&sort=company&q=shop');
  });

  it('offers the full CV once the master CV is ready', async () => {
    mockApi(routes({ '/api/tracker/cv': { ready: true, problems: [] } }));
    renderRoute('/tracker', <TrackerPage />);

    expect(await screen.findByRole('link', { name: 'Download full CV' })).toHaveAttribute('href', '/api/tracker/cv/full');
  });

  it('points to the example file when there is no master CV yet', async () => {
    mockApi(routes({ '/api/tracker/cv': { ready: false, problems: ['config/cv.json is missing.'] } }));
    renderRoute('/tracker', <TrackerPage />);

    expect(await screen.findByText(/config\/cv.example.json/)).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Download full CV' })).not.toBeInTheDocument();
  });

  it('lists the problems of a broken master CV instead of suggesting the example', async () => {
    mockApi(routes({ '/api/tracker/cv': { ready: false, problems: ['roles[0].id: "x" is used twice.'] } }));
    renderRoute('/tracker', <TrackerPage />);

    expect(await screen.findByText(/config\/cv.json has 1 problem/)).toBeInTheDocument();
    expect(screen.getByText('roles[0].id: "x" is used twice.')).toBeInTheDocument();
    expect(screen.queryByText(/cv.example.json/)).not.toBeInTheDocument();
  });

  it('shows the empty state', async () => {
    mockApi(routes({ '/api/tracker/applications': [], '/api/tracker/status': { ...STATUS, counts: {} } }));
    renderRoute('/tracker', <TrackerPage />);

    expect(await screen.findByText(/Nothing tracked yet/)).toBeInTheDocument();
  });

  it('marks a pasted job as pasted, not as missing from the latest run', async () => {
    const pasted: ApplicationRow = { ...ROWS[1], id: 'a3', title: 'Pasted Role', postingId: 'inbox:2027-03-01-td-dev.txt', live: false };
    mockApi(routes({ '/api/tracker/applications': [pasted] }));
    renderRoute('/tracker', <TrackerPage />);

    const row = (await screen.findByRole('link', { name: 'Pasted Role' })).closest('tr') as HTMLElement;

    expect(within(row).getByText('Pasted')).toBeInTheDocument();
    expect(within(row).queryByText(/Not in latest run/)).not.toBeInTheDocument();
  });
});
