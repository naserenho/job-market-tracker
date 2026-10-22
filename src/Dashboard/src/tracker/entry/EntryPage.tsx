import { useQuery } from '@tanstack/react-query';
import { Link, useParams } from 'react-router';
import { QueryState } from '../../components/States';
import { dateText, pct } from '../../format';
import { api } from '../../api';
import { tracker, type MarketState } from '../api';
import { dayText } from '../dates';
import { StatusChip } from '../StatusChip';
import { CompanyForm } from './CompanyForm';
import { Contacts } from './Contacts';
import { DeleteEntry } from './DeleteEntry';
import { DetailsForm } from './DetailsForm';
import { Documents } from './Documents';
import { FollowUps } from './FollowUps';
import { StatusForm } from './StatusForm';
import { Timeline } from './Timeline';

// What to say when the entry's posting is not counted in the market data, in the region's own words.
function notCounted(state: Exclude<MarketState, 'Counted'>, local: string, remote: string): string {
  switch (state) {
    case 'Pending': return `Not in the market data yet: it joins after the next rebuild if it's in ${local} or ${remote}.`;
    case 'OutOfScope': return `Not counted in the market data: the rebuild placed it outside ${local} and ${remote} roles.`;
    default: return 'Counted in the market data under another posting of the same job.';
  }
}

export function EntryPage() {
  const { id = '' } = useParams();
  const query = useQuery({ queryKey: ['tracker', 'entry', id], queryFn: () => tracker.detail(id) });
  const region = useQuery({ queryKey: ['meta'], queryFn: api.meta }).data?.region;
  if (!query.data) return <QueryState query={query} what="this entry" />;
  const entry = query.data;
  // A pasted job is seen once, when it was pasted: "live" and "not in the latest run" would both mislead.
  const pasted = entry.postingId?.startsWith('inbox:') ?? false;
  // The tracker names a pasted file "<date>-<company>-<title>.txt". The market data only knows the collection that
  // first counted it, which can be days later, so the date comes from the name.
  const pastedOn = /^inbox:(\d{4}-\d{2}-\d{2})-/.exec(entry.postingId ?? '')?.[1];

  return (
    <>
      <section className="section" aria-labelledby="h-entry">
        <p className="eyebrow"><Link to="/tracker">Tracker</Link> / {entry.company.name}</p>
        <h2 id="h-entry">{entry.title} <StatusChip status={entry.status} /></h2>
        <div className="facts">
          <span>{entry.company.name}</span>
          {entry.location && <span>{entry.location}</span>}
          {entry.url && <a href={entry.url} target="_blank" rel="noreferrer">Open the posting ↗</a>}
          {entry.market ? (
            <span>
              {pct(entry.market.match)} skill match ·{' '}
              {pasted
                ? (pastedOn ? `pasted ${dayText(pastedOn)}` : 'pasted')
                : `${entry.market.live ? 'still live' : 'not in the latest run'} · first seen ${dateText(entry.market.firstSeen)}`}
            </span>
          ) : entry.marketState && entry.marketState !== 'Counted' && (pasted || entry.marketState !== 'Pending') ? (
            <span>{notCounted(entry.marketState, region?.localLabel ?? 'the local area', region?.remoteLabel ?? 'remote in the country')}</span>
          ) : null}
        </div>
      </section>
      <StatusForm key={`status-${entry.status}`} entry={entry} />
      <div className="grid-2">
        <Timeline events={entry.events} />
        <FollowUps entry={entry} />
      </div>
      <DetailsForm key={`details-${entry.id}`} entry={entry} />
      <CompanyForm key={`company-${entry.company.id}`} company={entry.company} />
      <div className="grid-2">
        <Contacts entry={entry} />
        <Documents entry={entry} />
      </div>
      <details className="panel">
        <summary>Job text</summary>
        <pre className="posting-text">{entry.postingText || 'No job text saved.'}</pre>
      </details>
      <DeleteEntry entry={entry} />
    </>
  );
}
