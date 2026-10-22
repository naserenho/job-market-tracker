import { screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { PostingRow } from '../api';
import { mockApi, renderRoute } from '../test/render';
import { PostingList } from './PostingList';

const row: PostingRow = {
  id: 'ashby:S1', title: 'Senior Software Engineer', company: 'Cohere', location: 'Remote (Canada)',
  url: 'https://jobs.ashbyhq.com/cohere/S1', salaryMin: 150000, salaryMax: 190000, salaryPeriod: 'Year', match: 0.5,
  firstSeen: '2026-10-01T11:00:00Z', skills: ['Go', 'Kubernetes'], citizenshipOrClearance: true, source: 'ashby', isFullText: true,
};

describe('PostingList', () => {
  it('shows salary, match and the citizenship flag with a link to the posting', async () => {
    mockApi({ '/api/tracker/index': {} });
    renderRoute('/postings', <PostingList rows={[row]} caption="Examples" />);

    expect(screen.getByRole('link', { name: 'Senior Software Engineer' })).toHaveAttribute('href', row.url);
    expect(screen.getByText('$150k–$190k')).toBeInTheDocument();
    expect(screen.getByText('50%')).toBeInTheDocument();
    expect(screen.getByText(/citizenship/)).toBeInTheDocument();
    expect(await screen.findByRole('button', { name: 'Shortlist "Senior Software Engineer"' })).toBeInTheDocument();
  });

  it('says so when nothing matches', () => {
    mockApi({ '/api/tracker/index': {} });
    renderRoute('/postings', <PostingList rows={[]} caption="Examples" />);

    expect(screen.getByText(/No postings match these filters/)).toBeInTheDocument();
  });
});
