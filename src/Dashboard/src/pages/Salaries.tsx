import { useQuery } from '@tanstack/react-query';
import { useSearchParams } from 'react-router';
import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { api, type SalaryCell } from '../api';
import { PostingList } from '../components/PostingList';
import { Empty, QueryState } from '../components/States';
import { useFilters } from '../filters';
import { FAMILY_LABELS, SENIORITY_LABELS, label, money } from '../format';

const TOOLTIP_STYLE = { background: 'var(--surface)', border: '1px solid var(--rule)', color: 'var(--ink)' };

// 25th–75th percentile band with a median tick, on one scale shared by every row.
function RangeBar({ cell, lo, hi }: { cell: SalaryCell; lo: number; hi: number }) {
  if (cell.p25 === null || cell.p75 === null || cell.median === null) return null;
  const span = hi - lo || 1;
  const at = (value: number) => `${((value - lo) / span) * 100}%`;
  return (
    <div className="range" aria-hidden="true">
      <span className="iqr" style={{ left: at(cell.p25), width: `${((cell.p75 - cell.p25) / span) * 100}%` }} />
      <span className="median" style={{ left: at(cell.median) }} />
    </div>
  );
}

export function Salaries() {
  const [filters] = useFilters();
  const [params, setParams] = useSearchParams();
  const skill = params.get('skill') ?? '';
  const company = params.get('company') ?? '';
  const meta = useQuery({ queryKey: ['meta'], queryFn: api.meta });
  const skills = useQuery({ queryKey: ['skills', filters, 'all', ''], queryFn: () => api.skills(filters, 'all') });
  const query = useQuery({
    queryKey: ['salaries', filters, skill, company],
    queryFn: () => api.salaries(filters, skill || undefined, company || undefined),
  });

  const setParam = (key: string, value: string) =>
    setParams((current) => {
      const out = new URLSearchParams(current);
      if (value) out.set(key, value);
      else out.delete(key);
      return out;
    });

  const skillOptions = (skills.data?.skills ?? [])
    .filter((s) => s.count > 0 || s.skill === skill)
    .map((s) => s.skill)
    .sort((a, b) => a.localeCompare(b));
  const companyOptions = [...new Set([...(meta.data?.companies ?? []), ...(company ? [company] : [])])];

  const view = query.data;
  const measured = view?.cells.filter((c) => c.p25 !== null && c.p75 !== null) ?? [];
  const lo = measured.length > 0 ? Math.min(...measured.map((c) => c.p25 as number)) : 0;
  const hi = measured.length > 0 ? Math.max(...measured.map((c) => c.p75 as number)) : 0;
  const bands = view?.histogram.map((b) => ({ band: `${money(b.from)}–${money(b.to)}`, count: b.count })) ?? [];

  return (
    <section className="section" aria-labelledby="h-salaries">
      <h2 id="h-salaries">Salaries</h2>
      <div className="toolbar">
        <div className="field">
          <label className="field-label" htmlFor="salaries-skill">Skill</label>
          <select id="salaries-skill" value={skill} onChange={(e) => setParam('skill', e.target.value)}>
            <option value="">Any skill</option>
            {(skillOptions.includes(skill) || !skill ? skillOptions : [skill, ...skillOptions]).map((s) => <option key={s} value={s}>{s}</option>)}
          </select>
        </div>
        <div className="field">
          <label className="field-label" htmlFor="salaries-company">Company</label>
          <select id="salaries-company" value={company} onChange={(e) => setParam('company', e.target.value)}>
            <option value="">Any company</option>
            {companyOptions.map((c) => <option key={c} value={c}>{c}</option>)}
          </select>
        </div>
      </div>

      {!view ? (
        <QueryState query={query} what="salaries" />
      ) : view.withSalary === 0 ? (
        <Empty>No salaried postings match these filters. This fills in as new postings with a stated salary arrive.</Empty>
      ) : (
        <>
          <p className="muted small">
            {view.withSalary} of {view.postings} postings state a salary. Amounts are yearly midpoints in {meta.data?.region.currency ?? 'the local currency'}; hourly rates count 2,080 hours a year.
          </p>
          <div className="chart" aria-hidden="true">
            <ResponsiveContainer width="100%" height="100%">
              <BarChart data={bands} margin={{ left: 8, right: 16, top: 8, bottom: 4 }}>
                <CartesianGrid vertical={false} stroke="var(--rule)" />
                <XAxis dataKey="band" tick={{ fill: 'var(--muted)', fontSize: 12 }} stroke="var(--rule)" />
                <YAxis allowDecimals={false} tick={{ fill: 'var(--muted)', fontSize: 12 }} stroke="var(--rule)" />
                <Tooltip contentStyle={TOOLTIP_STYLE} />
                <Bar dataKey="count" name="Postings" fill="var(--bar)" />
              </BarChart>
            </ResponsiveContainer>
          </div>
          <table className="sr-only">
            <caption>Postings by yearly salary band</caption>
            <tbody>{bands.map((b) => <tr key={b.band}><th scope="row">{b.band}</th><td>{b.count}</td></tr>)}</tbody>
          </table>

          <div className="table-wrap">
            <table>
              <caption className="sr-only">Salary percentiles by role and seniority</caption>
              <thead>
                <tr>
                  <th>Role</th>
                  <th>Seniority</th>
                  <th className="num">Salaries</th>
                  <th className="num">25th</th>
                  <th className="num">Median</th>
                  <th className="num">75th</th>
                  <th aria-hidden="true" />
                </tr>
              </thead>
              <tbody>
                {view.cells.map((c) => (
                  <tr key={`${c.family}-${c.seniority}`}>
                    <td>{label(FAMILY_LABELS, c.family)}</td>
                    <td>{label(SENIORITY_LABELS, c.seniority)}</td>
                    <td className="num">{c.count}</td>
                    <td className="num">{money(c.p25)}</td>
                    <td className="num">{c.median === null ? <span className="muted">n &lt; 5</span> : money(c.median)}</td>
                    <td className="num">{money(c.p75)}</td>
                    <td><RangeBar cell={c} lo={lo} hi={hi} /></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <h3>Highest-paying postings</h3>
          <PostingList rows={view.examples} caption="Salaried postings, highest first" />
        </>
      )}
    </section>
  );
}
