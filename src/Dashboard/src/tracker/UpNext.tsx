import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router';
import { ErrorBanner } from '../components/States';
import { tracker, type UpNextItem } from './api';
import { dayText, localToday } from './dates';
import { useTrackerMutation } from './useTrackerMutation';

export function UpNext() {
  const items = useQuery({ queryKey: ['tracker', 'up-next'], queryFn: tracker.upNext });
  const done = useTrackerMutation((item: UpNextItem) =>
    tracker.editFollowUp(item.applicationId, item.followUpId, { action: item.action, dueOn: item.dueOn, doneOn: localToday() }));

  if (items.error) return <ErrorBanner error={items.error} />;
  if (!items.data) return null;
  return (
    <section className="upnext" aria-labelledby="h-upnext">
      <h3 id="h-upnext">Up next</h3>
      {items.data.length === 0 ? (
        <p className="empty">Nothing due this week.</p>
      ) : (
        <ul>
          {items.data.map((item) => (
            <li key={item.followUpId} className={item.overdue ? 'is-overdue' : undefined}>
              <span className="when">{item.overdue ? `Overdue · ${dayText(item.dueOn)}` : dayText(item.dueOn)}</span>
              <span className="what">{item.action}</span>
              <Link to={`/tracker/${item.applicationId}`}>{item.title} · {item.company}</Link>
              <button type="button" onClick={() => done.mutate(item)} disabled={done.isPending} aria-label={`Mark "${item.action}" done`}>Done</button>
            </li>
          ))}
        </ul>
      )}
      {done.error && <ErrorBanner error={done.error} />}
    </section>
  );
}
