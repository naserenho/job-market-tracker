import { fireEvent, screen, waitFor } from '@testing-library/react';
import { Link } from 'react-router';
import { describe, expect, it } from 'vitest';
import type { PostingPage } from '../api';
import { META, mockApi, renderRoute } from '../test/render';
import { Postings } from './Postings';

const page = (n: number, total = 60): PostingPage => ({
  total, page: n, pageSize: 25,
  items: [{
    id: `p${n}`, title: `Developer ${n}`, company: 'Acme', location: 'Toronto', url: 'https://x.test', salaryMin: null, salaryMax: null,
    salaryPeriod: null, match: 0.5, firstSeen: '2026-10-01T11:00:00Z', skills: ['C#'], citizenshipOrClearance: false, source: 'greenhouse', isFullText: true,
  }],
});

describe('Postings page', () => {
  it('searches titles and companies and starts again from page 1', async () => {
    const calls = mockApi({ '/api/meta': META, '/api/postings': page(3) });
    renderRoute('/postings?page=3', <Postings />);
    await screen.findByText('Page 3 of 3');

    fireEvent.change(screen.getByLabelText('Title or company'), { target: { value: 'bank' } });
    fireEvent.click(screen.getByRole('button', { name: 'Search' }));

    await waitFor(() => expect(calls).toContain('/api/postings?q=bank&sort=newest&page=1&pageSize=25'));
    expect(screen.getByTestId('location').textContent).toBe('/postings?q=bank');
  });

  it('pages through the results', async () => {
    const calls = mockApi({ '/api/meta': META, '/api/postings': page(1) });
    renderRoute('/postings', <Postings />);
    await screen.findByText('Page 1 of 3');

    fireEvent.click(screen.getByRole('button', { name: 'Next' }));

    await waitFor(() => expect(calls).toContain('/api/postings?sort=newest&page=2&pageSize=25'));
    expect(screen.getByText('Page 2 of 3')).toBeInTheDocument();
  });

  it('shows and clears a skill filter that came from another page', async () => {
    const calls = mockApi({ '/api/meta': META, '/api/postings': page(1, 1) });
    renderRoute('/postings?skill=CI%2FCD', <Postings />);

    const chip = await screen.findByRole('button', { name: 'Remove skill filter CI/CD' });
    expect(calls).toContain('/api/postings?skill=CI%2FCD&sort=newest&page=1&pageSize=25');
    fireEvent.click(chip);
    expect(screen.getByTestId('location').textContent).toBe('/postings');
  });

  it('shows the empty state', async () => {
    mockApi({ '/api/meta': META, '/api/postings': { total: 0, page: 1, pageSize: 25, items: [] } });
    renderRoute('/postings?where=remote&seniority=Junior', <Postings />);
    expect(await screen.findByText(/No postings match these filters/)).toBeInTheDocument();
  });

  it('follows the address when you go back or forward', async () => {
    mockApi({ '/api/meta': META, '/api/postings': page(1) });
    renderRoute('/postings?q=bank', <><Postings /><Link to="/postings?q=azure">history</Link></>);
    expect(await screen.findByLabelText('Title or company')).toHaveValue('bank');

    fireEvent.click(screen.getByRole('link', { name: 'history' }));

    await waitFor(() => expect(screen.getByLabelText('Title or company')).toHaveValue('azure'));
  });

  it('goes back to the last page when the results shrink', async () => {
    mockApi({ '/api/meta': META, '/api/postings': page(3, 30) });
    renderRoute('/postings?page=3', <Postings />);

    expect(await screen.findByText('Page 2 of 2')).toBeInTheDocument();
    await waitFor(() => expect(screen.getByTestId('location').textContent).toBe('/postings?page=2'));
  });

  // While the next search loads, the previous results stay on screen. Their page count says nothing about the new
  // search, so the page in the address must be left alone (and the history entry kept).
  it('keeps the page from the address while the previous results are still on screen', async () => {
    mockApi({ '/api/meta': META, '/api/postings?company=Small': page(1, 8), '/api/postings': page(3) });
    renderRoute('/postings?company=Small', <><Postings /><Link to="/postings?page=3">back</Link></>);
    await screen.findByText('8 postings');

    fireEvent.click(screen.getByRole('link', { name: 'back' }));

    expect(await screen.findByText('Page 3 of 3')).toBeInTheDocument();
    expect(screen.getByTestId('location').textContent).toBe('/postings?page=3');
  });
});
