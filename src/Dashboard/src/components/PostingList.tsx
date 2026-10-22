import type { PostingRow } from '../api';
import { dateText, pct, salaryText } from '../format';
import { ShortlistButton } from '../tracker/ShortlistButton';

export function PostingList({ rows, caption }: { rows: PostingRow[]; caption: string }) {
  if (rows.length === 0) return <p className="empty">No postings match these filters.</p>;
  return (
    <div className="table-wrap">
      <table>
        <caption className="sr-only">{caption}</caption>
        <thead>
          <tr>
            <th>Role</th>
            <th>Company</th>
            <th>Where</th>
            <th className="num">Salary</th>
            <th className="num">Match</th>
            <th className="num">First seen</th>
            <th>Track</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((r) => (
            <tr key={r.id}>
              <td>
                <a href={r.url} target="_blank" rel="noreferrer">{r.title}</a>
                {r.citizenshipOrClearance && <span className="flag"> · citizenship/clearance</span>}
                <div className="muted small">{r.skills.join(' · ')}</div>
              </td>
              <td>{r.company}</td>
              <td>{r.location}</td>
              <td className="num">{salaryText(r.salaryMin, r.salaryMax, r.salaryPeriod)}</td>
              <td className="num">{pct(r.match)}</td>
              <td className="num">{dateText(r.firstSeen)}</td>
              <td><ShortlistButton postingId={r.id} title={r.title} /></td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
