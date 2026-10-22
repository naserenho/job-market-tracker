import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router';
import { Bar, BarChart, CartesianGrid, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { api, type SkillScore } from '../api';
import { LevelChip } from '../components/LevelChip';
import { Empty, QueryState } from '../components/States';
import { linkSearch, useFilters, type Filters } from '../filters';
import { num, pct, salaryText } from '../format';
import { ShortlistButton } from '../tracker/ShortlistButton';

const TOOLTIP_STYLE = { background: 'var(--surface)', border: '1px solid var(--rule)', color: 'var(--ink)' };

function Stat({ label, value }: { label: string; value: number }) {
  return (
    <div className="stat">
      <span className="eyebrow">{label}</span>
      <span className="stat-value">{num(value)}</span>
    </div>
  );
}

function SkillRanking({ items, filters }: { items: SkillScore[]; filters: Filters }) {
  if (items.length === 0) return <Empty>Nothing to rank for these filters.</Empty>;
  return (
    <ol className="ranked">
      {items.map((s) => (
        <li key={s.skill}>
          <Link to={{ pathname: `/skills/${encodeURIComponent(s.skill)}`, search: linkSearch(filters) }}>{s.skill}</Link>{' '}
          <span className="muted small">{pct(s.demand)} of postings</span> <LevelChip level={s.level} />
        </li>
      ))}
    </ol>
  );
}

export function Overview() {
  const [filters] = useFilters();
  const query = useQuery({ queryKey: ['overview', filters], queryFn: () => api.overview(filters) });
  if (!query.data) return <QueryState query={query} what="the overview" />;

  const report = query.data.report;
  if (!report) return <Empty>No data yet: the first collection runs Tuesday or Friday at 07:00.</Empty>;
  const s = report.snapshot;
  const top = report.topSkills.slice(0, 15).map((d) => ({ skill: d.skill, share: Math.round(d.fullTextShare * 1000) / 10 }));

  return (
    <>
      <section className="section" aria-labelledby="h-snapshot">
        <h2 id="h-snapshot">Last {report.windowDays} days</h2>
        <div className="stats">
          <Stat label="Postings" value={s.inWindow} />
          <Stat label="Active now" value={s.activeNow} />
          <Stat label="New since last run" value={s.newSinceLastRun} />
          <Stat label="With salary" value={s.withSalary} />
          <Stat label="Full text" value={s.fullText} />
          <Stat label="Citizenship / clearance" value={s.citizenshipFlagged} />
        </div>
      </section>

      {s.inWindow === 0 ? (
        <Empty>No postings match these filters. Widen the window or add role families.</Empty>
      ) : (
        <>
          <section className="section" aria-labelledby="h-demand">
            <h2 id="h-demand">Most requested skills</h2>
            <p className="muted small">Share of full-text postings that mention each skill. The Skills page lists every skill.</p>
            {top.length === 0 ? <Empty>No full-text postings in this selection.</Empty> : (
              <>
              <div className="chart" style={{ height: 28 * top.length + 40 }} aria-hidden="true">
                <ResponsiveContainer width="100%" height="100%">
                  <BarChart data={top} layout="vertical" margin={{ left: 8, right: 24, top: 4, bottom: 4 }}>
                    <CartesianGrid horizontal={false} stroke="var(--rule)" />
                    <XAxis type="number" unit="%" tick={{ fill: 'var(--muted)', fontSize: 12 }} stroke="var(--rule)" />
                    <YAxis type="category" dataKey="skill" width={150} interval={0} tick={{ fill: 'var(--ink)', fontSize: 13 }} stroke="var(--rule)" />
                    <Tooltip contentStyle={TOOLTIP_STYLE} />
                    <Bar dataKey="share" name="Share (%)" fill="var(--bar)" />
                  </BarChart>
                </ResponsiveContainer>
              </div>
              <table className="sr-only">
                <caption>Most requested skills, share of full-text postings</caption>
                <tbody>{top.map((d) => <tr key={d.skill}><th scope="row">{d.skill}</th><td>{d.share}%</td></tr>)}</tbody>
              </table>
              </>
            )}
          </section>

          <div className="grid-2">
            <section className="section" aria-labelledby="h-gaps">
              <h2 id="h-gaps">Biggest gaps</h2>
              <p className="muted small">Asked for often, and not yet a strength of yours.</p>
              <SkillRanking items={report.gaps} filters={filters} />
            </section>
            <section className="section" aria-labelledby="h-strengths">
              <h2 id="h-strengths">Strengths in demand</h2>
              <p className="muted small">Your skills that postings ask for most.</p>
              <SkillRanking items={report.strengths} filters={filters} />
            </section>
          </div>

          <section className="section" aria-labelledby="h-matches">
            <h2 id="h-matches">Best matches right now</h2>
            {report.matches.length === 0 ? <Empty>No active posting lists three or more skills in this selection.</Empty> : (
              <div className="table-wrap">
                <table>
                  <thead>
                    <tr><th>Role</th><th>Company</th><th className="num">Match</th><th>Missing</th><th className="num">Salary</th><th>Track</th></tr>
                  </thead>
                  <tbody>
                    {report.matches.map((m) => (
                      <tr key={`${m.url}|${m.title}`}>
                        <td>
                          <a href={m.url} target="_blank" rel="noreferrer">{m.title}</a>
                          {m.citizenshipOrClearance && <span className="flag"> · citizenship/clearance</span>}
                          <div className="muted small">{m.location}</div>
                        </td>
                        <td>{m.company}</td>
                        <td className="num">{pct(m.score)}</td>
                        <td className="small">{m.missing.join(', ') || '—'}</td>
                        <td className="num">{m.salary ? salaryText(m.salary.min, m.salary.max, m.salary.period) : '—'}</td>
                        <td><ShortlistButton postingId={m.postingId} title={m.title} /></td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>

          {report.trends && (
            <section className="section" aria-labelledby="h-trend">
              <h2 id="h-trend">Postings seen per week</h2>
              <div className="chart" aria-hidden="true">
                <ResponsiveContainer width="100%" height="100%">
                  <LineChart data={report.trends.weekly} margin={{ left: 8, right: 24, top: 8, bottom: 4 }}>
                    <CartesianGrid vertical={false} stroke="var(--rule)" />
                    <XAxis dataKey="weekStart" tick={{ fill: 'var(--muted)', fontSize: 12 }} stroke="var(--rule)" />
                    <YAxis allowDecimals={false} tick={{ fill: 'var(--muted)', fontSize: 12 }} stroke="var(--rule)" />
                    <Tooltip contentStyle={TOOLTIP_STYLE} />
                    <Line type="monotone" dataKey="active" name="Postings" stroke="var(--accent)" strokeWidth={2} dot={false} />
                  </LineChart>
                </ResponsiveContainer>
              </div>
              <table className="sr-only">
                <caption>Postings seen per week</caption>
                <tbody>{report.trends.weekly.map((w) => <tr key={w.weekStart}><th scope="row">Week of {w.weekStart}</th><td>{w.active}</td></tr>)}</tbody>
              </table>
            </section>
          )}

          <section className="section" aria-labelledby="h-companies">
            <h2 id="h-companies">Companies hiring</h2>
            <ul className="pills">
              {report.companies.map((c) => (
                <li key={c.company}>
                  <Link to={{ pathname: '/postings', search: linkSearch(filters, { company: c.company }) }}>
                    {c.company}<span className="count">{c.postings}</span>
                  </Link>
                </li>
              ))}
            </ul>
          </section>
        </>
      )}

      {report.warnings.length > 0 && (
        <section className="section" aria-labelledby="h-warnings">
          <h2 id="h-warnings">Data warnings</h2>
          <ul className="small">{report.warnings.map((w) => <li key={w}>{w}</li>)}</ul>
        </section>
      )}
    </>
  );
}
