import { fireEvent, screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { mockApi, renderRoute, reply, sentBody } from '../test/render';
import { localToday } from './dates';
import { UpNext } from './UpNext';

describe('Up next', () => {
  it("marks a follow-up done with today's date", async () => {
    const calls = mockApi({
      '/api/tracker/up-next': [{ followUpId: 'f1', applicationId: 'a1', title: 'Senior .NET Developer', company: 'RBC', action: 'Email Sam', dueOn: '2027-03-01', overdue: true }],
      'PATCH /api/tracker/applications/a1/follow-ups/f1': reply(204),
    });
    renderRoute('/tracker', <UpNext />);

    fireEvent.click(await screen.findByRole('button', { name: 'Mark "Email Sam" done' }));

    await waitFor(() => expect(calls).toContain('PATCH /api/tracker/applications/a1/follow-ups/f1'));
    expect(sentBody('PATCH /api/tracker/applications/a1/follow-ups/f1')).toEqual({ action: 'Email Sam', dueOn: '2027-03-01', doneOn: localToday() });
    expect(screen.getByText(/Overdue/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Senior .NET Developer · RBC' })).toHaveAttribute('href', '/tracker/a1');
  });

  it('says when nothing is due', async () => {
    mockApi({ '/api/tracker/up-next': [] });
    renderRoute('/tracker', <UpNext />);

    expect(await screen.findByText('Nothing due this week.')).toBeInTheDocument();
  });
});
