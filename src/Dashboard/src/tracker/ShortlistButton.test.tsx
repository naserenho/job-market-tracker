import { fireEvent, screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { failure, mockApi, renderRoute, reply } from '../test/render';
import type { TrackerIndex } from './api';
import { ShortlistButton } from './ShortlistButton';

describe('Shortlist button', () => {
  it('shortlists a posting, then links to its entry', async () => {
    let index: TrackerIndex = {};
    const calls = mockApi({
      '/api/tracker/index': () => index,
      'POST /api/tracker/applications': () => {
        index = { 'greenhouse:101': { id: 'a1', status: 'Shortlisted' } };
        return reply(201, { id: 'a1' });
      },
    });
    renderRoute('/postings', <ShortlistButton postingId="greenhouse:101" title="Senior Dev" />);

    const button = await screen.findByRole('button', { name: 'Shortlist "Senior Dev"' });
    await waitFor(() => expect(button).toBeEnabled()); // disabled until the tracker index has loaded
    fireEvent.click(button);

    expect(await screen.findByRole('link', { name: 'Shortlisted: open "Senior Dev" in the tracker' })).toHaveAttribute('href', '/tracker/a1');
    expect(calls).toContain('POST /api/tracker/applications');
  });

  it('shows the status of an entry that already exists', async () => {
    mockApi({ '/api/tracker/index': { 'ashby:S1': { id: 'a2', status: 'Interviewing' } } });
    renderRoute('/postings', <ShortlistButton postingId="ashby:S1" title="Platform Engineer" />);

    expect(await screen.findByRole('link', { name: /^Interviewing: open "Platform Engineer"/ })).toHaveAttribute('href', '/tracker/a2');
  });

  it('links to the existing entry when the API says it is already tracked', async () => {
    mockApi({ '/api/tracker/index': {}, 'POST /api/tracker/applications': failure(409, 'This posting is already in your tracker.', { id: 'a3' }) });
    renderRoute('/postings', <ShortlistButton postingId="lever:L1" title="Backend Developer" />);

    const button = await screen.findByRole('button', { name: 'Shortlist "Backend Developer"' });
    await waitFor(() => expect(button).toBeEnabled());
    fireEvent.click(button);

    expect(await screen.findByRole('link', { name: /open "Backend Developer"/ })).toHaveAttribute('href', '/tracker/a3');
  });

  it('stays out of the way when the tracker is unavailable', async () => {
    mockApi({ '/api/tracker/index': failure(503, 'The tracker database isn’t running.') });
    const { container } = renderRoute('/postings', <ShortlistButton postingId="x:1" title="X" />);

    await waitFor(() => expect(container.querySelector('button')).toBeNull());
  });

  it('shows why a posting could not be shortlisted', async () => {
    mockApi({ '/api/tracker/index': {}, 'POST /api/tracker/applications': failure(404, "Posting 'x:9' isn't in the current market data.") });
    renderRoute('/postings', <ShortlistButton postingId="x:9" title="Gone Job" />);

    const button = await screen.findByRole('button', { name: 'Shortlist "Gone Job"' });
    await waitFor(() => expect(button).toBeEnabled());
    fireEvent.click(button);

    expect(await screen.findByRole('alert')).toHaveTextContent("isn't in the current market data");
  });
});
