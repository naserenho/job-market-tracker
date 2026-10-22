import { fireEvent, screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { failure, mockApi, renderRoute, reply, sentBody } from '../test/render';
import { PasteJob } from './PasteJob';

const fill = (label: string, value: string) => fireEvent.change(screen.getByLabelText(label), { target: { value } });

describe('Paste a job', () => {
  it('checks the fields before sending anything', async () => {
    const calls = mockApi({ '/api/tracker/companies': [] });
    renderRoute('/tracker/new', <PasteJob />);

    fill('Link', 'www.rbc.com/job');
    fireEvent.click(screen.getByRole('button', { name: 'Save to tracker' }));

    expect(await screen.findByText('Title is required.')).toBeInTheDocument();
    expect(screen.getByText('Company is required.')).toBeInTheDocument();
    expect(screen.getByText(/Paste the job text/)).toBeInTheDocument();
    expect(screen.getByText(/starting with https/)).toBeInTheDocument();
    expect(screen.getByLabelText('Title')).toHaveAttribute('aria-invalid', 'true');
    expect(calls.filter((c) => c.startsWith('POST'))).toEqual([]);
  });

  it('saves the job and opens the new entry', async () => {
    mockApi({ '/api/tracker/companies': [{ id: 'c1', name: 'RBC' }], 'POST /api/tracker/applications/paste': reply(201, { id: 'a9' }) });
    renderRoute('/tracker/new', <PasteJob />);

    fill('Title', 'Senior .NET Developer');
    fill('Company', 'RBC');
    fill('Job text', 'C# and Azure.');
    fill('Starting status', 'Applied');
    fireEvent.click(screen.getByRole('button', { name: 'Save to tracker' }));

    await waitFor(() => expect(screen.getByTestId('location').textContent).toBe('/tracker/a9'));
    expect(sentBody('POST /api/tracker/applications/paste')).toEqual({
      title: 'Senior .NET Developer', company: 'RBC', url: '', location: '', postingText: 'C# and Azure.', status: 'Applied',
    });
  });

  it("shows the API's message next to the field it names", async () => {
    mockApi({
      '/api/tracker/companies': [],
      'POST /api/tracker/applications/paste': failure(400, 'Job text is longer than 100000 characters.', { field: 'postingText' }),
    });
    renderRoute('/tracker/new', <PasteJob />);

    fill('Title', 'Dev');
    fill('Company', 'Acme');
    fill('Job text', 'x');
    fireEvent.click(screen.getByRole('button', { name: 'Save to tracker' }));

    expect(await screen.findByText('Job text is longer than 100000 characters.')).toBeInTheDocument();
  });
});
