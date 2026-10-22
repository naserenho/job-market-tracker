import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { Link, useParams } from 'react-router';
import { ApiError } from '../../api';
import { QueryState } from '../../components/States';
import { tracker, type CvChoices, type CvProposal, type GeneratedCv } from '../api';
import { dayText } from '../dates';
import { Field } from '../Field';
import { SaveError } from '../SaveError';
import { useTrackerMutation } from '../useTrackerMutation';
import { CoveragePanel } from './CoveragePanel';
import { startDownload } from './download';
import { PostingTextForm } from './PostingTextForm';
import { SectionPicker } from './SectionPicker';
import { bulletsShowing, coverage, findable, initialChoices, length, move, PAGE_LIMIT, toggle } from './tailor';

type Made = GeneratedCv & { entryId: string };

export function TailorPage() {
  const { id = '' } = useParams();
  const [made, setMade] = useState<Made | null>(null);
  const status = useQuery({ queryKey: ['tracker', 'cv', 'status'], queryFn: tracker.cvStatus });
  const ready = status.data?.ready === true;
  const proposal = useQuery({ queryKey: ['tracker', 'cv', 'proposal', id], queryFn: () => tracker.proposal(id), enabled: ready });

  if (!status.data) return <QueryState query={status} what="your master CV" />;
  if (!ready) {
    return (
      <section className="section" aria-labelledby="h-tailor">
        <p className="eyebrow"><Link to={`/tracker/${id}`}>Back to the entry</Link></p>
        <h2 id="h-tailor">Tailor CV</h2>
        <div className="banner" role="alert">
          <p>Your master CV needs fixing before you can tailor it:</p>
          <ul>{status.data.problems.map((p, i) => <li key={i}>{p}</li>)}</ul>
        </div>
      </section>
    );
  }
  if (!proposal.data) return <QueryState query={proposal} what="the CV proposal" />;
  // Keyed by entry only: a refetch (another tab's follow-up, a saved job text, a generated CV) updates the coverage and
  // scores but never throws away unsaved ticks. Reset to suggestion takes the new suggestion.
  return (
    <Tailor key={proposal.data.entryId} proposal={proposal.data}
      made={made?.entryId === proposal.data.entryId ? made : null} onMade={setMade} />
  );
}

function Tailor({ proposal, made, onMade }: { proposal: CvProposal; made: Made | null; onMade: (made: Made) => void }) {
  const queryClient = useQueryClient();
  const [choices, setStored] = useState(() => initialChoices(proposal));
  const [removed, setRemoved] = useState(0);
  const [highlight, setHighlight] = useState<string | null>(null);
  const generate = useTrackerMutation(
    (sent: CvChoices) => tracker.generateCv(proposal.entryId, sent),
    (result) => {
      onMade({ ...result, entryId: proposal.entryId });
      startDownload(tracker.documentUrl(result.documentId));
    },
    // A refusal usually means cv.json changed under the page: fetch the proposal again.
    () => void queryClient.invalidateQueries({ queryKey: ['tracker', 'cv', 'proposal', proposal.entryId] }),
  );

  // A bullet can leave cv.json while this page is open; the refetched proposal no longer has it. Its tick is removed
  // here, once, and counted for the notice below.
  const known = new Set(proposal.sections.flatMap((s) => s.bullets.map((b) => b.id)));
  if (choices.bulletIds.some((id) => !known.has(id))) {
    const kept = choices.bulletIds.filter((id) => known.has(id));
    setRemoved(removed + choices.bulletIds.length - kept.length);
    setStored({ ...choices, bulletIds: kept });
  }
  // Any edit clears the last refusal and the dropped-bullets notice: both described choices that have now changed.
  const setChoices = (next: CvChoices) => {
    setStored(next);
    setRemoved(0);
    if (generate.isError) generate.reset();
  };
  // The refusal that led to the notice says "reload the page"; the notice replaces it.
  const explained = removed > 0 && generate.error instanceof ApiError && generate.error.details.field === 'bulletIds';

  const states = coverage(proposal, choices.bulletIds);
  const pointable = findable(proposal, choices.bulletIds);
  const wanted = new Set(proposal.postingSkills.map((s) => s.skill));
  // Only while the skill still needs finding: once a ticked bullet shows it, the highlight has done its job.
  const active = highlight && pointable.has(highlight) && states.get(highlight) !== 'bullet' ? highlight : null;
  const highlighted = active ? bulletsShowing(proposal, active) : new Set<string>();
  const size = length(proposal, choices);
  const summaryId = proposal.summaries.find((s) => s.text === choices.summary)?.id ?? '';
  const dropped = proposal.saved?.droppedIds.length ?? 0;

  return (
    <section className="section" aria-labelledby="h-tailor">
      <p className="eyebrow"><Link to={`/tracker/${proposal.entryId}`}>{proposal.company}</Link> / Tailor CV</p>
      <h2 id="h-tailor">Tailor CV: {proposal.title}</h2>
      {proposal.saved && <p className="hint">Starting from the CV you generated on {dayText(proposal.saved.generatedOn)}.</p>}
      {dropped > 0 && (
        <div className="banner warn" role="status">
          {dropped} {dropped === 1 ? 'bullet' : 'bullets'} from your last CV {dropped === 1 ? 'is' : 'are'} no longer in cv.json.
        </div>
      )}
      <div className="tailor">
        <div className="tailor-side">
          {(proposal.thin || proposal.textSource === 'entry') && (
            <PostingTextForm entryId={proposal.entryId} version={proposal.version} textSource={proposal.textSource}
              skillCount={proposal.postingSkills.length} thin={proposal.thin} />
          )}
          <CoveragePanel proposal={proposal} states={states} findable={pointable} highlight={active} onHighlight={setHighlight} />
          <p className={size.pages > PAGE_LIMIT ? 'field-error' : 'hint'} role="status">
            About {size.pages} pages ({size.words} words){size.pages > PAGE_LIMIT ? ': over 2 pages, so consider unticking a few bullets.' : ''}
          </p>
        </div>
        <div className="tailor-main">
          <div className="panel form-grid">
            <Field id="cv-headline" label="Headline" wide>
              <input id="cv-headline" type="text" value={choices.headline}
                onChange={(e) => setChoices({ ...choices, headline: e.target.value })} />
            </Field>
            <Field id="cv-variant" label="Summary variant">
              <select id="cv-variant" value={summaryId} onChange={(e) => {
                const picked = proposal.summaries.find((s) => s.id === e.target.value);
                if (picked) setChoices({ ...choices, summary: picked.text });
              }}>
                {summaryId === '' && <option value="">Edited</option>}
                {proposal.summaries.map((s) => (
                  <option key={s.id} value={s.id}>{s.id}{s.id === proposal.suggestedSummaryId ? ' (suggested)' : ''}</option>
                ))}
              </select>
            </Field>
            <Field id="cv-summary" label="Summary" wide>
              <textarea id="cv-summary" rows={4} value={choices.summary}
                onChange={(e) => setChoices({ ...choices, summary: e.target.value })} />
            </Field>
          </div>
          <section className="panel" aria-labelledby="h-cv-skills">
            <h3 id="h-cv-skills">Skills section</h3>
            <ul className="list">
              {proposal.skillGroups.map((g) => <li key={g.name}><strong>{g.name}:</strong> {g.items.map((i) => i.text).join(', ')}</li>)}
            </ul>
          </section>
          {proposal.sections.map((section) => (
            <SectionPicker key={section.id} section={section} choices={choices} wanted={wanted} highlighted={highlighted}
              onToggle={(bulletId) => {
                setChoices(toggle(choices, bulletId));
                setHighlight(null);
              }}
              onMove={(bulletId, delta) => setChoices(move(choices, section, bulletId, delta))} />
          ))}
          <div className="actions">
            <button type="button" onClick={() => setChoices(proposal.suggestion)}>Reset to suggestion</button>
            <button type="button" className="primary" disabled={generate.isPending || !choices.headline.trim()}
              onClick={() => {
                setRemoved(0);
                generate.mutate(choices);
              }}>Generate .docx</button>
          </div>
          {made && (
            <p role="status">
              Saved on this entry as <a href={tracker.documentUrl(made.documentId)}>{made.fileName}</a>. If you edit it in Word before
              sending, upload the final file on the entry page.
            </p>
          )}
          {removed > 0 && (
            <div className="banner warn" role="status">
              {removed} {removed === 1 ? 'bullet' : 'bullets'} you had ticked {removed === 1 ? 'is' : 'are'} no longer in
              cv.json and {removed === 1 ? 'was' : 'were'} removed. Check the CV, then generate again.
            </div>
          )}
          {!explained && <SaveError error={generate.error} />}
        </div>
      </div>
    </section>
  );
}
