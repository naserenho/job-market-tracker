import { describe, expect, it, vi } from 'vitest';
import { ApiError, api } from './api';
import { DEFAULT_FILTERS } from './filters';
import { failure, mockApi } from './test/render';

describe('api client', () => {
  it.each([
    ['C#', '/api/skills/C%23?text=full'],
    ['CI/CD', '/api/skills/CI%2FCD?text=full'],
    ['Estimation & Planning', '/api/skills/Estimation%20%26%20Planning?text=full'],
  ])('encodes the skill name %s in the path', async (name, expected) => {
    const calls = mockApi({ '/api/skills/': { skill: name } });
    await api.skill(name, DEFAULT_FILTERS, 'full');
    expect(calls[0]).toBe(expected);
  });

  it('turns problem details into an ApiError with the detail text', async () => {
    mockApi({ '/api/meta': failure(503, "The tracker database isn't running.") });
    await expect(api.meta()).rejects.toMatchObject({ status: 503, message: "The tracker database isn't running." });
  });

  it('explains when the API cannot be reached at all', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => {
      throw new TypeError('Failed to fetch');
    }));
    await expect(api.meta()).rejects.toBeInstanceOf(ApiError);
  });

  it('says the API is not reachable when the dev proxy answers 500 without problem details', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response('', { status: 500 })));
    await expect(api.meta()).rejects.toMatchObject({ status: 500, message: expect.stringMatching(/not reachable/) });
  });

  it('keeps the status for other errors without a body', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response('', { status: 404 })));
    await expect(api.meta()).rejects.toMatchObject({ message: 'The API answered 404.' });
  });
});
