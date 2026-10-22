import { act, fireEvent, screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { ABU_DHABI_META, META, failure, mockApi, renderRoute, reply, sentBody } from '../../test/render';
import type { ApplicationDetail } from '../api';
import { localToday } from '../dates';
import { EntryPage } from './EntryPage';

const ENTRY: ApplicationDetail = {
  id: 'a1', title: 'Senior .NET Developer', url: 'https://jobs.rbc.com/1', location: 'Toronto, ON', status: 'Applied', origin: 'Market',
  foundVia: 'LinkedIn', workModel: 'Hybrid', officeDays: '3 days', teamSize: '8', workingHours: '9 to 5', salaryPosted: 'CAD 130–150k',
  salaryAsked: '', salaryOffered: '', notes: '', postingId: 'greenhouse:101', postingText: 'We need C# and Azure.',
  market: { match: 0.75, live: true, firstSeen: '2027-02-20T12:00:00Z', lastSeen: '2027-03-01T12:00:00Z', skills: ['.NET', 'C#'] },
  marketState: 'Counted',
  company: { id: 'c1', name: 'RBC', website: '', officeLocation: '', companySize: '', annualLeave: '', benefits: '', notes: '', version: 7, applications: 1 },
  events: [
    { id: 'e1', onDate: '2027-02-25', fromStatus: null, toStatus: 'Shortlisted', note: 'Added from the market data' },
    { id: 'e2', onDate: '2027-03-01', fromStatus: 'Shortlisted', toStatus: 'Applied', note: '' },
  ],
  followUps: [{ id: 'f1', action: 'Email Sam', dueOn: '2027-03-08', doneOn: null }],
  contacts: [], documents: [], createdAt: '2027-02-25T12:00:00Z', updatedAt: '2027-03-01T12:00:00Z', version: 42,
};

const open = () => renderRoute('/tracker/a1', <EntryPage />, '/tracker/:id');

describe('Entry page', () => {
  it('shows the entry with its market facts, timeline and follow-ups', async () => {
    mockApi({ '/api/tracker/applications/a1': ENTRY });
    open();

    expect(await screen.findByRole('heading', { level: 2, name: /Senior .NET Developer/ })).toBeInTheDocument();
    expect(screen.getByText(/75% skill match · still live/)).toBeInTheDocument();
    expect(screen.getByText(/Added as/)).toBeInTheDocument();
    expect(screen.getByText(/Email Sam/)).toBeInTheDocument();
    expect(screen.getByDisplayValue('CAD 130–150k')).toBeInTheDocument();
  });

  it('records a status change with its date and note', async () => {
    mockApi({ '/api/tracker/applications/a1': ENTRY, 'POST /api/tracker/applications/a1/status': reply(204) });
    open();

    fireEvent.change(await screen.findByLabelText('New status'), { target: { value: 'Screening' } });
    fireEvent.change(screen.getByLabelText('Note'), { target: { value: 'Recruiter call booked' } });
    fireEvent.click(screen.getByRole('button', { name: 'Update status' }));

    await waitFor(() => expect(sentBody('POST /api/tracker/applications/a1/status'))
      .toEqual({ status: 'Screening', on: localToday(), note: 'Recruiter call booked' }));
  });

  it('offers Reload when the details changed in another tab', async () => {
    const calls = mockApi({
      '/api/tracker/applications/a1': ENTRY,
      'PATCH /api/tracker/applications/a1': failure(409, 'This entry changed in another tab. Reload to see the latest.'),
    });
    open();

    fireEvent.change(await screen.findByLabelText('Team size'), { target: { value: '9' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save details' }));

    expect(await screen.findByText(/changed in another tab/)).toBeInTheDocument();
    expect(sentBody('PATCH /api/tracker/applications/a1')).toMatchObject({ version: 42, teamSize: '9', workModel: 'Hybrid' });
    const loads = calls.filter((c) => c === '/api/tracker/applications/a1').length;
    fireEvent.click(screen.getByRole('button', { name: 'Reload' }));
    await waitFor(() => expect(calls.filter((c) => c === '/api/tracker/applications/a1').length).toBeGreaterThan(loads));
  });

  it('keeps unsaved details when another change refreshes the entry', async () => {
    let version = 42;
    const added = { id: 'f2', action: 'Send thank-you', dueOn: '2027-03-10', doneOn: null };
    mockApi({
      '/api/tracker/applications/a1': () => ({ ...ENTRY, version, followUps: version === 43 ? [...ENTRY.followUps, added] : ENTRY.followUps }),
      'POST /api/tracker/applications/a1/follow-ups': () => { version = 43; return reply(201, { id: 'f2' }); },
      'PATCH /api/tracker/applications/a1': reply(204),
    });
    open();

    fireEvent.change(await screen.findByLabelText('Team size'), { target: { value: '9' } });
    fireEvent.change(screen.getByLabelText('Next action'), { target: { value: 'Send thank-you' } });
    fireEvent.change(screen.getByLabelText('Due'), { target: { value: '2027-03-10' } });
    fireEvent.click(screen.getByRole('button', { name: 'Add follow-up' }));
    await screen.findByText(/Send thank-you/); // the refreshed entry (version 43) is on screen

    expect(screen.getByLabelText('Team size')).toHaveValue('9');
    fireEvent.click(screen.getByRole('button', { name: 'Save details' }));
    await waitFor(() => expect(sentBody('PATCH /api/tracker/applications/a1')).toMatchObject({ version: 43, teamSize: '9' }));
  });

  it('keeps unsaved details on a real conflict, and Reload takes the latest values', async () => {
    let current: ApplicationDetail = ENTRY;
    mockApi({
      '/api/tracker/applications/a1': () => current,
      'PATCH /api/tracker/applications/a1': failure(409, 'This entry changed in another tab. Reload to see the latest.'),
    });
    const { client } = open();

    fireEvent.change(await screen.findByLabelText('Team size'), { target: { value: '9' } });
    current = { ...ENTRY, version: 43, teamSize: '12', title: 'Senior .NET Developer II' }; // saved in another tab
    await act(async () => { await client.invalidateQueries({ queryKey: ['tracker'] }); });
    await screen.findByRole('heading', { level: 2, name: /Developer II/ });

    expect(screen.getByLabelText('Team size')).toHaveValue('9');
    fireEvent.click(screen.getByRole('button', { name: 'Save details' }));
    expect(await screen.findByText(/changed in another tab/)).toBeInTheDocument();
    expect(sentBody('PATCH /api/tracker/applications/a1')).toMatchObject({ version: 42, teamSize: '9' });
    fireEvent.click(screen.getByRole('button', { name: 'Reload' }));
    await waitFor(() => expect(screen.getByLabelText('Team size')).toHaveValue('12'));
  });

  it('adds a follow-up', async () => {
    mockApi({ '/api/tracker/applications/a1': ENTRY, 'POST /api/tracker/applications/a1/follow-ups': reply(201, { id: 'f2' }) });
    open();

    fireEvent.change(await screen.findByLabelText('Next action'), { target: { value: 'Send thank-you' } });
    fireEvent.change(screen.getByLabelText('Due'), { target: { value: '2027-03-10' } });
    fireEvent.click(screen.getByRole('button', { name: 'Add follow-up' }));

    await waitFor(() => expect(sentBody('POST /api/tracker/applications/a1/follow-ups'))
      .toEqual({ action: 'Send thank-you', dueOn: '2027-03-10', doneOn: null }));
  });

  it('refuses files over 10 MB before uploading', async () => {
    const calls = mockApi({ '/api/tracker/applications/a1': ENTRY });
    open();

    const big = new File([new Uint8Array(10 * 1024 * 1024 + 1)], 'scan.pdf', { type: 'application/pdf' });
    fireEvent.change(await screen.findByLabelText('File'), { target: { files: [big] } });
    fireEvent.click(screen.getByRole('button', { name: 'Upload' }));

    expect(await screen.findByText('Files can be up to 10 MB.')).toBeInTheDocument();
    expect(calls.filter((c) => c.startsWith('POST'))).toEqual([]);
  });

  it('marks generated CVs and links to the Tailor page', async () => {
    const documents = [
      { id: 'd1', kind: 'Cv', label: 'Tailored CV', fileName: 'cv.docx', sizeBytes: 2048, sentOn: '2027-03-01', generated: true },
      { id: 'd2', kind: 'Cv', label: 'Final CV', fileName: 'cv.pdf', sizeBytes: 2048, sentOn: '2027-03-02', generated: false },
    ];
    mockApi({ '/api/tracker/applications/a1': { ...ENTRY, documents } });
    open();

    expect(await screen.findByRole('link', { name: 'Tailor CV' })).toHaveAttribute('href', '/tracker/a1/cv');
    expect(screen.getByText(/generated Mar\.? 1/)).toBeInTheDocument();
    expect(screen.getByText(/sent Mar\.? 2/)).toBeInTheDocument();
  });

  it('deletes only after a confirmation inside the page', async () => {
    const calls = mockApi({ '/api/tracker/applications/a1': ENTRY, 'DELETE /api/tracker/applications/a1': reply(204) });
    open();

    fireEvent.click(await screen.findByRole('button', { name: 'Delete this entry' }));
    expect(calls).not.toContain('DELETE /api/tracker/applications/a1');
    fireEvent.click(screen.getByRole('button', { name: 'Delete for good' }));

    await waitFor(() => expect(screen.getByTestId('location').textContent).toBe('/tracker'));
    expect(calls).toContain('DELETE /api/tracker/applications/a1');
  });

  it('offers no Reload when a company rename clashes with another company', async () => {
    mockApi({
      '/api/tracker/applications/a1': ENTRY,
      'PATCH /api/tracker/companies/c1': failure(409, "Another company in your tracker is already called 'TD'.", { id: 'c2' }),
    });
    open();

    fireEvent.change(await screen.findByLabelText('Company name'), { target: { value: 'TD' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save company' }));

    expect(await screen.findByText(/already called 'TD'/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Reload' })).not.toBeInTheDocument();
  });

  it.each([
    ['Pending', /joins after the next rebuild/],
    ['OutOfScope', /outside GTA and Remote \(Canada\) roles/],
    ['Duplicate', /under another posting of the same job/],
  ] as const)('explains a pasted job that is %s in the market data', async (marketState, text) => {
    mockApi({ '/api/meta': META, '/api/tracker/applications/a1': { ...ENTRY, origin: 'Pasted', postingId: 'inbox:2027-02-25-rbc-dev.txt', market: null, marketState } });
    open();
    expect(await screen.findByText(text)).toBeInTheDocument();
  });

  it('says when a counted pasted job was pasted, not whether it is live', async () => {
    mockApi({ '/api/tracker/applications/a1': { ...ENTRY, origin: 'Pasted', postingId: 'inbox:2027-02-25-rbc-dev.txt' } });
    open();

    expect(await screen.findByText(/75% skill match · pasted/)).toBeInTheDocument();
    expect(screen.queryByText(/still live|not in the latest run/)).not.toBeInTheDocument();
  });

  // The market data only knows the collection that first counted the paste, which can be days after it.
  it('dates a pasted job by the day it was pasted, not by the collection that counted it', async () => {
    mockApi({ '/api/tracker/applications/a1': { ...ENTRY, origin: 'Pasted', postingId: 'inbox:2025-02-25-rbc-dev.txt' } });
    open();

    expect(await screen.findByText(/75% skill match · pasted Feb.? 25, 2025/)).toBeInTheDocument();
  });

  it('says only "pasted" for a hand-saved inbox file without a date in its name', async () => {
    mockApi({ '/api/tracker/applications/a1': { ...ENTRY, postingId: 'inbox:rbc-senior-dev.txt' } });
    open();

    expect(await screen.findByText(/75% skill match · pasted$/)).toBeInTheDocument();
  });

  it('names the region when a pasted job waits for the next rebuild', async () => {
    mockApi({
      '/api/meta': ABU_DHABI_META,
      '/api/tracker/applications/a1': { ...ENTRY, origin: 'Pasted', postingId: 'inbox:2027-02-25-adnoc-dev.txt', market: null, marketState: 'Pending' },
    });
    open();

    expect(await screen.findByText(/if it's in Abu Dhabi or Remote \(UAE\)/)).toBeInTheDocument();
  });
});
