import { useQuery } from '@tanstack/react-query';
import { Link, useParams, useSearchParams } from 'react-router';
import { api, type TextKind } from '../api';
import { LevelChip } from '../components/LevelChip';
import { PostingList } from '../components/PostingList';
import { Empty, QueryState } from '../components/States';
import { linkSearch, useFilters } from '../filters';
import { money, pct } from '../format';

const TEXT_KINDS: TextKind[] = ['full', 'snippet', 'all'];

// React Router decodes the path segment, %2F included, so "CI/CD" arrives whole. Decoding once more is harmless and
// covers a name that was encoded twice.
export function decodeSkill(raw: string): string {
  try {
    return decodeURIComponent(raw);
  } catch {
    return raw;
  }
}

export function SkillDetail() {
  const [filters] = useFilters();
  const [params] = useSearchParams();
  const name = decodeSkill(useParams().name ?? '');
  const text = TEXT_KINDS.find((t) => t === params.get('text')) ?? 'full';
  const query = useQuery({ queryKey: ['skill', name, filters, text], queryFn: () => api.skill(name, filters, text) });

  if (!query.data) return <QueryState query={query} what={name} />;
  const d = query.data;
  const search = linkSearch(filters);

  return (
    <>
      <section className="section" aria-labelledby="h-skill">
        <p className="eyebrow"><Link to={{ pathname: '/skills', search }}>Skills</Link> / {d.category}</p>
        <h2 id="h-skill">{d.skill} <LevelChip level={d.level} /></h2>
        <div className="stats">
          <div className="stat"><span className="eyebrow">Share of postings</span><span className="stat-value">{pct(d.share)}</span></div>
          <div className="stat"><span className="eyebrow">Postings</span><span className="stat-value">{d.count} of {d.postings}</span></div>
          <div className="stat">
            <span className="eyebrow">Median salary with it</span>
            <span className="stat-value">{money(d.salary.withMedian)}</span>
            <span className="muted small">{d.salary.withCount} postings with a salary</span>
          </div>
          <div className="stat">
            <span className="eyebrow">Median salary without it</span>
            <span className="stat-value">{money(d.salary.withoutMedian)}</span>
            <span className="muted small">{d.salary.withoutCount} postings with a salary</span>
          </div>
        </div>
        <p className="muted small">A median needs at least 5 salaries; "—" means there aren't enough yet.</p>
      </section>

      <div className="grid-2">
        <section className="section" aria-labelledby="h-co">
          <h2 id="h-co">Often asked for together</h2>
          {d.coSkills.length === 0 ? <Empty>No other skills appear with it in this selection.</Empty> : (
            <ul className="pills">
              {d.coSkills.map((c) => (
                <li key={c.name}>
                  <Link to={{ pathname: `/skills/${encodeURIComponent(c.name)}`, search }}>{c.name}<span className="count">{c.count}</span></Link>
                </li>
              ))}
            </ul>
          )}
        </section>
        <section className="section" aria-labelledby="h-companies">
          <h2 id="h-companies">Companies asking for it</h2>
          {d.companies.length === 0 ? <Empty>No company in this selection mentions it.</Empty> : (
            <ul className="pills">
              {d.companies.map((c) => (
                <li key={c.name}>
                  <Link to={{ pathname: '/postings', search: linkSearch(filters, { skill: d.skill, company: c.name }) }}>
                    {c.name}<span className="count">{c.count}</span>
                  </Link>
                </li>
              ))}
            </ul>
          )}
        </section>
      </div>

      <section className="section" aria-labelledby="h-examples">
        <h2 id="h-examples">Postings that mention it</h2>
        <PostingList rows={d.examples} caption={`Postings that mention ${d.skill}`} />
        {d.count > d.examples.length && (
          <p className="small">
            <Link to={{ pathname: '/postings', search: linkSearch(filters, { skill: d.skill }) }}>Browse all postings with {d.skill}</Link>
          </p>
        )}
      </section>
    </>
  );
}
