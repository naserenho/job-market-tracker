import { describe, expect, it, vi } from 'vitest';
import { ApiError } from '../api';
import { failure, mockApi, reply, sentBody } from '../test/render';
import { tracker } from './api';

describe('tracker client', () => {
  it('sends writes as JSON with the dashboard header', async () => {
    mockApi({ 'POST /api/tracker/applications': reply(201, { id: 'a1' }) });

    await expect(tracker.shortlist('greenhouse:101')).resolves.toEqual({ id: 'a1' });

    const [, init] = vi.mocked(fetch).mock.calls[0];
    expect(init?.headers).toMatchObject({ 'X-Tracker-Client': 'dashboard', 'Content-Type': 'application/json' });
    expect(sentBody('POST /api/tracker/applications')).toEqual({ postingId: 'greenhouse:101' });
  });

  it('builds list queries from the non-empty parameters', async () => {
    const calls = mockApi({ '/api/tracker/applications': [] });

    await tracker.list({ status: 'Applied', q: '', sort: 'company' });

    expect(calls).toEqual(['/api/tracker/applications?status=Applied&sort=company']);
  });

  it('resolves 204 answers with nothing', async () => {
    mockApi({ 'PATCH /api/tracker/applications/a1': reply(204) });

    await expect(tracker.editFollowUp('a1', 'f1', { action: 'Call', dueOn: '2027-03-01', doneOn: null })).resolves.toBeUndefined();
  });

  it('keeps the problem details of a refusal', async () => {
    mockApi({ 'POST /api/tracker/applications': failure(409, 'This posting is already in your tracker.', { id: 'a7' }) });

    const error = await tracker.shortlist('ashby:S1').catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).status).toBe(409);
    expect((error as ApiError).details.id).toBe('a7');
  });

  it('uploads documents as form data without a JSON content type', async () => {
    mockApi({ 'POST /api/tracker/applications/a1/documents': reply(201, { id: 'd1' }) });
    const form = new FormData();
    form.append('file', new Blob(['%PDF']), 'CV.pdf');

    await tracker.upload('a1', form);

    const [, init] = vi.mocked(fetch).mock.calls[0];
    expect(init?.body).toBeInstanceOf(FormData);
    expect(init?.headers).toEqual({ Accept: 'application/json', 'X-Tracker-Client': 'dashboard' });
  });
});
