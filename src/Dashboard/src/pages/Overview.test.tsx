import { screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { ReportModel } from '../api';
import { mockApi, renderRoute } from '../test/render';
import { Overview } from './Overview';

const REPORT: ReportModel = {
  generatedAt: '2026-10-01T12:00:00Z', latestRunId: '2026-10-01_0700', latestRunAt: '2026-10-01T11:00:00Z', windowDays: 30,
  snapshot: {
    activeNow: 40, newSinceLastRun: 5, inWindow: 120, fullText: 80, snippets: 39, withSalary: 60, citizenshipFlagged: 3, runs: 3, historyDays: 30,
    bySource: { greenhouse: 80, adzuna: 40 },
  },
  topSkills: [{ skill: 'Python', category: 'language', fullTextShare: 0.4, snippetShare: 0.1, count: 32, level: 'None' }],
  gaps: [{ skill: 'CI/CD', category: 'devops', demand: 0.3, level: 'Learning', score: 0.2 }],
  strengths: [{ skill: 'C#', category: 'language', demand: 0.2, level: 'Strong', score: 0.2 }],
  salaries: [],
  companies: [{ company: 'Acme Inc.', postings: 7 }],
  matches: [{
    title: 'Senior .NET Developer', company: 'Acme Inc.', url: 'https://x.test/1', location: 'Toronto', score: 0.75, skillCount: 4,
    missing: ['Kubernetes'], salary: { min: 130000, max: 150000, period: 'Year' }, citizenshipOrClearance: true, postingId: 'greenhouse:101',
  }],
  trends: null,
  warnings: ['Inbox file a.txt has no Location line.'],
};

describe('Overview page', () => {
  it('shows the snapshot, rankings, matches, companies and warnings', async () => {
    mockApi({ '/api/overview': { report: REPORT }, '/api/tracker/index': {} });
    renderRoute('/?where=gta', <Overview />);

    expect(await screen.findByRole('heading', { name: 'Last 30 days' })).toBeInTheDocument();
    expect(screen.getByText('120')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'CI/CD' })).toHaveAttribute('href', '/skills/CI%2FCD?where=local');
    expect(screen.getByRole('link', { name: 'Senior .NET Developer' })).toHaveAttribute('href', 'https://x.test/1');
    expect(screen.getByText(/citizenship\/clearance/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Acme Inc\./ })).toHaveAttribute('href', '/postings?where=local&company=Acme+Inc.');
    expect(screen.getByText('Inbox file a.txt has no Location line.')).toBeInTheDocument();
    expect(await screen.findByRole('button', { name: 'Shortlist "Senior .NET Developer"' })).toBeInTheDocument();
  });

  it('says when nothing has been collected yet', async () => {
    mockApi({ '/api/overview': { report: null } });
    renderRoute('/', <Overview />);
    expect(await screen.findByText(/No data yet/)).toBeInTheDocument();
  });

  it('says when the filters match nothing', async () => {
    mockApi({ '/api/overview': { report: { ...REPORT, snapshot: { ...REPORT.snapshot, inWindow: 0 }, warnings: [] } } });
    renderRoute('/', <Overview />);
    expect(await screen.findByText(/No postings match these filters/)).toBeInTheDocument();
  });
});
