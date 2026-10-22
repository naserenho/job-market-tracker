import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { Link, useSearchParams } from 'react-router';
import { api, type SkillRow, type TextKind } from '../api';
import { LevelChip } from '../components/LevelChip';
import { Empty, QueryState } from '../components/States';
import { linkSearch, useFilters } from '../filters';
import { num, pct, signedPts } from '../format';

type SortKey = 'skill' | 'category' | 'share' | 'level' | 'change';

const TEXT_LABELS: Record<TextKind, string> = { full: 'Full-text postings', snippet: 'Adzuna snippets', all: 'All postings' };
const LEVEL_ORDER = { None: 0, Learning: 1, Working: 2, Strong: 3 } as const;

function sortValue(row: SkillRow, key: SortKey): string | number {
  switch (key) {
    case 'skill': return row.skill.toLowerCase();
    case 'category': return row.category;
    case 'share': return row.share;
    case 'level': return LEVEL_ORDER[row.level];
    case 'change': return row.change ?? Number.NEGATIVE_INFINITY;
  }
}

function sortSkills(rows: SkillRow[], key: SortKey, descending: boolean): SkillRow[] {
  return [...rows].sort((a, b) => {
    const [x, y] = [sortValue(a, key), sortValue(b, key)];
    const order = x < y ? -1 : x > y ? 1 : 0;
    return (descending ? -order : order) || a.skill.localeCompare(b.skill);
  });
}

export function Skills() {
  const [filters] = useFilters();
  const [params, setParams] = useSearchParams();
  const text = (Object.keys(TEXT_LABELS) as TextKind[]).find((t) => t === params.get('text')) ?? 'full';
  const category = params.get('category') ?? '';
  const [sort, setSort] = useState<{ key: SortKey; descending: boolean }>({ key: 'share', descending: true });
  const meta = useQuery({ queryKey: ['meta'], queryFn: api.meta });
  const query = useQuery({
    queryKey: ['skills', filters, text, category],
    queryFn: () => api.skills(filters, text, category || undefined),
  });

  const setParam = (key: string, value: string, fallback: string) =>
    setParams((current) => {
      const out = new URLSearchParams(current);
      if (value === fallback) out.delete(key);
      else out.set(key, value);
      return out;
    });

  const header = (key: SortKey, title: string, numeric = false) => (
    <th className={numeric ? 'num' : undefined} aria-sort={sort.key === key ? (sort.descending ? 'descending' : 'ascending') : 'none'}>
      <button
        type="button"
        className="sort"
        onClick={() => setSort((s) => ({ key, descending: s.key === key ? !s.descending : key !== 'skill' && key !== 'category' }))}
      >
        {title}{sort.key === key ? (sort.descending ? ' ↓' : ' ↑') : ''}
      </button>
    </th>
  );

  // Skills nobody mentions stay listed only when you have them, so your own profile is always visible.
  const rows = query.data
    ? sortSkills(query.data.skills.filter((s) => s.count > 0 || s.level !== 'None'), sort.key, sort.descending)
    : [];
  const search = linkSearch(filters);

  return (
    <section className="section" aria-labelledby="h-skills">
      <h2 id="h-skills">Skill demand</h2>
      <div className="toolbar">
        <div className="field">
          <label className="field-label" htmlFor="skills-text">Counted in</label>
          <select id="skills-text" value={text} onChange={(e) => setParam('text', e.target.value, 'full')}>
            {(Object.keys(TEXT_LABELS) as TextKind[]).map((t) => <option key={t} value={t}>{TEXT_LABELS[t]}</option>)}
          </select>
        </div>
        <div className="field">
          <label className="field-label" htmlFor="skills-category">Category</label>
          <select id="skills-category" value={category} onChange={(e) => setParam('category', e.target.value, '')}>
            <option value="">All categories</option>
            {meta.data?.categories.map((c) => <option key={c} value={c}>{c}</option>)}
          </select>
        </div>
      </div>

      {!query.data ? (
        <QueryState query={query} what="skills" />
      ) : query.data.postings === 0 ? (
        <Empty>No postings match these filters. Widen the window, add role families or count snippets too.</Empty>
      ) : (
        <>
          <p className="muted small">
            {num(query.data.postings)} postings counted. Change compares with the previous window of the same length.
          </p>
          <div className="table-wrap">
            <table>
              <caption className="sr-only">Skills by share of postings</caption>
              <thead>
                <tr>
                  {header('skill', 'Skill')}
                  {header('category', 'Category')}
                  {header('share', 'Share', true)}
                  <th aria-hidden="true" />
                  <th className="num">Postings</th>
                  {header('level', 'You')}
                  {header('change', 'Change', true)}
                </tr>
              </thead>
              <tbody>
                {rows.map((r) => (
                  <tr key={r.skill}>
                    <td><Link to={{ pathname: `/skills/${encodeURIComponent(r.skill)}`, search }}>{r.skill}</Link></td>
                    <td className="muted">{r.category}</td>
                    <td className="num">{pct(r.share)}</td>
                    <td><div className="bar"><span style={{ width: `${Math.round(r.share * 100)}%` }} /></div></td>
                    <td className="num">{r.count}</td>
                    <td><LevelChip level={r.level} /></td>
                    <td className={`num ${r.change === null ? 'muted' : r.change > 0 ? 'change-up' : r.change < 0 ? 'change-down' : ''}`}>
                      {signedPts(r.change)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}
    </section>
  );
}
