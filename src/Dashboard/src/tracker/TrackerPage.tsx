import { useQuery } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router';
import { Empty, ErrorBanner, Loading } from '../components/States';
import { dateText } from '../format';
import { STATUSES, tracker, type TrackerSort } from './api';
import { dayText, localToday } from './dates';
import { StatusChip } from './StatusChip';
import { UpNext } from './UpNext';

const SORTS: Record<TrackerSort, string> = { next: 'Next action first', updated: 'Recently updated', company: 'Company' };

// The API's one problem for a master CV that doesn't exist yet (MasterCvFile.Missing).
const cvMissing = (problems: string[]) => problems.length === 1 && problems[0].startsWith('config/cv.json is missing');

export function TrackerPage() {
  const [params, setParams] = useSearchParams();
  const status = STATUSES.find((s) => s === params.get('status')) ?? '';
  const q = params.get('q') ?? '';
  const sort = (Object.keys(SORTS) as TrackerSort[]).find((s) => s === params.get('sort')) ?? 'next';
  const [draft, setDraft] = useState(q);
  useEffect(() => setDraft(q), [q]);
  const summary = useQuery({ queryKey: ['tracker', 'status'], queryFn: tracker.status });
  const cv = useQuery({ queryKey: ['tracker', 'cv', 'status'], queryFn: tracker.cvStatus });
  const rows = useQuery({ queryKey: ['tracker', 'list', status, q, sort], queryFn: () => tracker.list({ status, q, sort }) });

  const set = (patch: Record<string, string>) =>
    setParams((current) => {
      const out = new URLSearchParams(current);
      Object.entries(patch).forEach(([key, value]) => (value ? out.set(key, value) : out.delete(key)));
      return out;
    });

  const counts = summary.data?.counts ?? {};
  const today = localToday();

  return (
    <section className="section" aria-labelledby="h-tracker">
      <div className="tracker-head">
        <h2 id="h-tracker">Tracker</h2>
        <div className="actions">
          {cv.data?.ready && <a className="button" href={tracker.fullCvUrl}>Download full CV</a>}
          {cv.data && !cv.data.ready && (cvMissing(cv.data.problems) ? (
            <span className="hint">To tailor CVs, copy config/cv.example.json to config/cv.json and fill it in.</span>
          ) : (
            // The owner's own file exists but has mistakes: never suggest copying the example over it.
            <details className="hint">
              <summary>config/cv.json has {cv.data.problems.length} {cv.data.problems.length === 1 ? 'problem' : 'problems'} to fix before you can tailor CVs</summary>
              <ul>{cv.data.problems.map((p, i) => <li key={i}>{p}</li>)}</ul>
            </details>
          ))}
          <Link className="button" to="/tracker/new">Paste a job</Link>
        </div>
      </div>
      <ul className="pills" aria-label="Entries by status">
        {STATUSES.filter((s) => counts[s]).map((s) => (
          <li key={s}>
            <button type="button" aria-pressed={status === s} onClick={() => set({ status: status === s ? '' : s })}>
              {s}<span className="count">{counts[s]}</span>
            </button>
          </li>
        ))}
      </ul>
      <UpNext />
      <form className="toolbar" role="search" onSubmit={(e) => { e.preventDefault(); set({ q: draft.trim() }); }}>
        <div className="field">
          <label className="field-label" htmlFor="tracker-q">Role or company</label>
          <input id="tracker-q" type="search" value={draft} onChange={(e) => setDraft(e.target.value)} />
        </div>
        <button type="submit">Search</button>
        <div className="field">
          <label className="field-label" htmlFor="tracker-status">Status</label>
          <select id="tracker-status" value={status} onChange={(e) => set({ status: e.target.value })}>
            <option value="">All statuses</option>
            {STATUSES.map((s) => <option key={s} value={s}>{s}</option>)}
          </select>
        </div>
        <div className="field">
          <label className="field-label" htmlFor="tracker-sort">Sort</label>
          <select id="tracker-sort" value={sort} onChange={(e) => set({ sort: e.target.value === 'next' ? '' : e.target.value })}>
            {(Object.keys(SORTS) as TrackerSort[]).map((s) => <option key={s} value={s}>{SORTS[s]}</option>)}
          </select>
        </div>
      </form>
      {rows.error && <ErrorBanner error={rows.error} />}
      {!rows.data ? (
        !rows.error && <Loading what="your tracker" />
      ) : rows.data.length === 0 ? (
        <Empty>{status || q ? 'Nothing matches these filters.' : 'Nothing tracked yet. Shortlist postings from the Postings page, or paste a job.'}</Empty>
      ) : (
        <div className="table-wrap">
          <table>
            <caption className="sr-only">Tracked postings and applications</caption>
            <thead>
              <tr><th>Role</th><th>Company</th><th>Status</th><th>Next action</th><th className="num">Updated</th><th>Posting</th></tr>
            </thead>
            <tbody>
              {rows.data.map((r) => (
                <tr key={r.id}>
                  <td><Link to={`/tracker/${r.id}`}>{r.title}</Link></td>
                  <td>{r.company}</td>
                  <td><StatusChip status={r.status} /></td>
                  <td>
                    {r.next
                      ? <span className={r.next.dueOn < today ? 'overdue' : undefined}>{r.next.action} · {dayText(r.next.dueOn)}</span>
                      : <span className="muted">—</span>}
                  </td>
                  <td className="num">{dateText(r.updatedAt)}</td>
                  <td>
                    {r.postingId === null ? <span className="muted">No posting</span>
                      : r.postingId.startsWith('inbox:') ? <span className="muted">Pasted</span>
                      : r.live ? <span className="live">● Live</span>
                      : <span className="muted">○ Not in latest run</span>}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}
