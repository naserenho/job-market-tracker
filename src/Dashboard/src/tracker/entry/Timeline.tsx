import type { EventView } from '../api';
import { dayText } from '../dates';

export function Timeline({ events }: { events: EventView[] }) {
  return (
    <section className="panel" aria-labelledby="h-timeline">
      <h3 id="h-timeline">Timeline</h3>
      <ol className="timeline">
        {events.map((e) => (
          <li key={e.id}>
            <span className="muted small">{dayText(e.onDate)}</span>{' '}
            {e.fromStatus ? <>{e.fromStatus} → <strong>{e.toStatus}</strong></> : <>Added as <strong>{e.toStatus}</strong></>}
            {e.note && <span className="muted"> · {e.note}</span>}
          </li>
        ))}
      </ol>
    </section>
  );
}
