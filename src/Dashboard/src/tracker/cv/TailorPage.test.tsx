import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { failure, mockApi, renderRoute, reply, sentBody } from '../../test/render';
import { startDownload } from './download';
import { PROPOSAL } from './sample';
import { TailorPage } from './TailorPage';

vi.mock('./download', () => ({ startDownload: vi.fn() }));

const READY = { ready: true, problems: [] };
const open = () => renderRoute('/tracker/a1/cv', <TailorPage />, '/tracker/:id/cv');
const group = (title: string) => screen.getByRole('heading', { level: 4, name: new RegExp(`^${title}`) }).parentElement as HTMLElement;

describe('Tailor CV page', () => {
  beforeEach(() => vi.mocked(startDownload).mockClear());

  it('shows the suggestion and moves skills between groups as bullets are ticked', async () => {
    mockApi({ '/api/tracker/cv': READY, '/api/tracker/applications/a1/cv': PROPOSAL });
    open();

    expect(await screen.findByRole('checkbox', { name: 'Ran Docker in production.' })).toBeChecked();
    expect(screen.getByRole('checkbox', { name: 'Moved services to Kubernetes.' })).not.toBeChecked();
    expect(within(group('Available')).getByRole('button', { name: 'Kubernetes' })).toBeInTheDocument();
    expect(within(group('Not in your CV')).getByText(/add a true bullet to cv.json/)).toBeInTheDocument();

    fireEvent.click(screen.getByRole('checkbox', { name: 'Moved services to Kubernetes.' }));

    expect(within(group('In a bullet')).getByText('Kubernetes')).toBeInTheDocument();
    expect(screen.getByText(/3 of 2 bullets: more than cv.json suggests/)).toBeInTheDocument();
  });

  it('highlights the bullets that show an available skill', async () => {
    mockApi({ '/api/tracker/cv': READY, '/api/tracker/applications/a1/cv': PROPOSAL });
    open();

    fireEvent.click(await screen.findByRole('button', { name: 'Kubernetes' }));

    expect(screen.getByRole('checkbox', { name: 'Moved services to Kubernetes.' }).closest('li')).toHaveClass('highlight');
    expect(screen.getByRole('checkbox', { name: 'Ran Docker in production.' }).closest('li')).not.toHaveClass('highlight');
  });

  it('generates with the edited headline and reordered bullets, then downloads', async () => {
    mockApi({
      '/api/tracker/cv': READY,
      '/api/tracker/applications/a1/cv': PROPOSAL,
      'POST /api/tracker/applications/a1/cv': reply(201, { documentId: 'd9', fileName: 'Alex Example - CV - RBC.docx' }),
    });
    open();

    fireEvent.change(await screen.findByLabelText('Headline'), { target: { value: 'Senior .NET Engineer' } });
    fireEvent.click(screen.getByRole('button', { name: 'Move up: Ran Docker in production.' }));
    fireEvent.click(screen.getByRole('button', { name: 'Generate .docx' }));

    await waitFor(() => expect(startDownload).toHaveBeenCalledWith('/api/tracker/documents/d9'));
    expect(sentBody('POST /api/tracker/applications/a1/cv')).toEqual({
      headline: 'Senior .NET Engineer', summary: 'Builds .NET systems.', bulletIds: ['a2', 'a1', 'g1'],
    });
    expect(await screen.findByRole('link', { name: 'Alex Example - CV - RBC.docx' })).toHaveAttribute('href', '/api/tracker/documents/d9');
  });

  it('resets to the suggestion after changes', async () => {
    mockApi({ '/api/tracker/cv': READY, '/api/tracker/applications/a1/cv': PROPOSAL });
    open();

    fireEvent.click(await screen.findByRole('checkbox', { name: 'Ran Docker in production.' }));
    expect(screen.getByRole('checkbox', { name: 'Ran Docker in production.' })).not.toBeChecked();
    fireEvent.click(screen.getByRole('button', { name: 'Reset to suggestion' }));

    expect(screen.getByRole('checkbox', { name: 'Ran Docker in production.' })).toBeChecked();
  });

  it('starts from the last generated CV and says when bullets left cv.json', async () => {
    const saved = { generatedOn: '2027-03-01', headline: 'Lead dev', summary: 'Leads small teams.', bulletIds: ['a3'], droppedIds: ['gone-1'] };
    mockApi({ '/api/tracker/cv': READY, '/api/tracker/applications/a1/cv': { ...PROPOSAL, saved } });
    open();

    expect(await screen.findByLabelText('Headline')).toHaveValue('Lead dev');
    expect(screen.getByRole('checkbox', { name: 'Moved services to Kubernetes.' })).toBeChecked();
    expect(screen.getByRole('checkbox', { name: 'Ran Docker in production.' })).not.toBeChecked();
    expect(screen.getByLabelText('Summary variant')).toHaveValue('lead');
    expect(screen.getByText(/1 bullet from your last CV is no longer in cv.json/)).toBeInTheDocument();
  });

  it('asks for the full job text when the posting is only a snippet', async () => {
    mockApi({
      '/api/tracker/cv': READY,
      '/api/tracker/applications/a1/cv': { ...PROPOSAL, thin: true, textSource: 'market' },
      'PUT /api/tracker/applications/a1/posting-text': reply(204),
    });
    open();

    fireEvent.change(await screen.findByLabelText('Full job text'), { target: { value: 'The whole posting: C#, Kubernetes.' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save job text' }));

    await waitFor(() => expect(sentBody('PUT /api/tracker/applications/a1/posting-text'))
      .toEqual({ postingText: 'The whole posting: C#, Kubernetes.', version: 42 }));
  });

  it('keeps unsaved ticks when the entry changes in another tab', async () => {
    let current = PROPOSAL;
    mockApi({ '/api/tracker/cv': READY, '/api/tracker/applications/a1/cv': () => current });
    const { client } = open();

    fireEvent.click(await screen.findByRole('checkbox', { name: 'Moved services to Kubernetes.' }));
    current = { ...PROPOSAL, version: 43, title: 'Senior .NET Developer II' }; // saved in another tab: a new entry version
    await act(async () => { await client.invalidateQueries({ queryKey: ['tracker'] }); });
    await screen.findByRole('heading', { level: 2, name: /Developer II/ });

    expect(screen.getByRole('checkbox', { name: 'Moved services to Kubernetes.' })).toBeChecked();
  });

  it('lets you replace or clear job text saved on the entry', async () => {
    mockApi({
      '/api/tracker/cv': READY,
      '/api/tracker/applications/a1/cv': { ...PROPOSAL, textSource: 'entry', thin: false },
      'PUT /api/tracker/applications/a1/posting-text': reply(204),
    });
    open();

    expect(await screen.findByText('Replace the job text')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Clear the saved text' }));

    await waitFor(() => expect(sentBody('PUT /api/tracker/applications/a1/posting-text')).toEqual({ postingText: '', version: 42 }));
    expect(await screen.findByText('Saved job text cleared.')).toBeInTheDocument();
  });

  it('says how few skills were found when the saved text is still thin', async () => {
    const twoSkills = PROPOSAL.postingSkills.slice(0, 2);
    mockApi({ '/api/tracker/cv': READY, '/api/tracker/applications/a1/cv': { ...PROPOSAL, textSource: 'entry', thin: true, postingSkills: twoSkills } });
    open();

    expect(await screen.findByText(/Only 2 skills were found in the job text you saved/)).toBeInTheDocument();
    expect(screen.queryByText(/only has a short snippet/)).not.toBeInTheDocument();
  });

  it('lists master CV problems instead of the page', async () => {
    const calls = mockApi({ '/api/tracker/cv': { ready: false, problems: ['roles[0].id: "x" is used twice.'] } });
    open();

    expect(await screen.findByText('roles[0].id: "x" is used twice.')).toBeInTheDocument();
    expect(calls).not.toContain('/api/tracker/applications/a1/cv');
  });

  it('shows the estimated length and warns past two pages', async () => {
    mockApi({ '/api/tracker/cv': READY, '/api/tracker/applications/a1/cv': { ...PROPOSAL, fixedWords: 1000 } });
    open();

    // 1000 + 3 + 3 + 11 = 1017 words; 1017 / 450 = 2.26
    expect(await screen.findByText(/About 2.3 pages \(1017 words\): over 2 pages/)).toBeInTheDocument();
  });

  it('points a listed skill at the unticked bullet that proves it', async () => {
    const withAzureBullet = {
      ...PROPOSAL,
      sections: PROPOSAL.sections.map((s) => s.id !== 'acme' ? s : {
        ...s, bullets: s.bullets.map((b) => b.id === 'a3' ? { ...b, skills: ['Kubernetes', 'Azure'] } : b),
      }),
    };
    mockApi({ '/api/tracker/cv': READY, '/api/tracker/applications/a1/cv': withAzureBullet });
    open();
    await screen.findByRole('checkbox', { name: 'Ran Docker in production.' });

    fireEvent.click(within(group('Listed')).getByRole('button', { name: 'Azure' }));

    expect(screen.getByRole('checkbox', { name: 'Moved services to Kubernetes.' }).closest('li')).toHaveClass('highlight');
  });

  it('keeps a listed skill as plain text when no unticked bullet shows it', async () => {
    mockApi({ '/api/tracker/cv': READY, '/api/tracker/applications/a1/cv': PROPOSAL });
    open();
    await screen.findByRole('checkbox', { name: 'Ran Docker in production.' });

    expect(within(group('Listed')).queryByRole('button', { name: 'Azure' })).not.toBeInTheDocument();
    expect(within(group('Listed')).getByText('Azure')).toBeInTheDocument();
  });

  it('drops the highlight once its bullet is ticked, and does not bring it back', async () => {
    mockApi({ '/api/tracker/cv': READY, '/api/tracker/applications/a1/cv': PROPOSAL });
    open();
    const bullet = () => screen.getByRole('checkbox', { name: 'Moved services to Kubernetes.' });

    fireEvent.click(await screen.findByRole('button', { name: 'Kubernetes' }));
    expect(bullet().closest('li')).toHaveClass('highlight');
    fireEvent.click(bullet());
    expect(bullet().closest('li')).not.toHaveClass('highlight');
    fireEvent.click(bullet());

    expect(bullet().closest('li')).not.toHaveClass('highlight');
  });

  it('drops a bullet that left cv.json and lets you generate again', async () => {
    let current = PROPOSAL;
    const withoutA2 = { ...PROPOSAL, sections: PROPOSAL.sections.map((s) => ({ ...s, bullets: s.bullets.filter((b) => b.id !== 'a2') })) };
    let refused = false;
    mockApi({
      '/api/tracker/cv': READY,
      '/api/tracker/applications/a1/cv': () => current,
      'POST /api/tracker/applications/a1/cv': () => {
        if (refused) return reply(201, { documentId: 'd9', fileName: 'CV.docx' });
        refused = true;
        current = withoutA2;
        return failure(400, "Bullet 'a2' isn't in config/cv.json any more. Reload the page to see your current CV.", { field: 'bulletIds' });
      },
    });
    open();

    fireEvent.click(await screen.findByRole('button', { name: 'Generate .docx' }));
    expect(await screen.findByText(/1 bullet you had ticked is no longer in cv.json/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Generate .docx' }));

    await waitFor(() => expect(startDownload).toHaveBeenCalled());
    expect(sentBody('POST /api/tracker/applications/a1/cv')).toMatchObject({ bulletIds: ['a1', 'g1'] });
  });

  // The dropped-bullets notice is about one refusal: it must not outlive the next attempt or hide its result.
  const droppingA2 = () => {
    let current = PROPOSAL;
    const withoutA2 = { ...PROPOSAL, sections: PROPOSAL.sections.map((s) => ({ ...s, bullets: s.bullets.filter((b) => b.id !== 'a2') })) };
    let refused = false;
    return (second: () => unknown) => ({
      '/api/tracker/cv': READY,
      '/api/tracker/applications/a1/cv': () => current,
      'POST /api/tracker/applications/a1/cv': () => {
        if (refused) return second();
        refused = true;
        current = withoutA2;
        return failure(400, "Bullet 'a2' isn't in config/cv.json any more. Reload the page to see your current CV.", { field: 'bulletIds' });
      },
    });
  };

  it('takes the dropped-bullets notice away once the CV is generated', async () => {
    mockApi(droppingA2()(() => reply(201, { documentId: 'd9', fileName: 'CV.docx' })));
    open();

    fireEvent.click(await screen.findByRole('button', { name: 'Generate .docx' }));
    await screen.findByText(/1 bullet you had ticked is no longer in cv.json/);
    expect(screen.queryByText(/Reload the page/)).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Generate .docx' }));

    expect(await screen.findByRole('link', { name: 'CV.docx' })).toBeInTheDocument();
    expect(screen.queryByText(/no longer in cv.json/)).not.toBeInTheDocument();
  });

  it('shows a later failure instead of the dropped-bullets notice', async () => {
    mockApi(droppingA2()(() => failure(503, "The tracker database isn't running.")));
    open();

    fireEvent.click(await screen.findByRole('button', { name: 'Generate .docx' }));
    await screen.findByText(/1 bullet you had ticked is no longer in cv.json/);
    fireEvent.click(screen.getByRole('button', { name: 'Generate .docx' }));

    expect(await screen.findByText(/database isn't running/)).toBeInTheDocument();
    expect(screen.queryByText(/no longer in cv.json/)).not.toBeInTheDocument();
  });
});
