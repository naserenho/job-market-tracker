import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { useEffect, useState, type FormEvent } from 'react';
import { useSearchParams } from 'react-router';
import { api, type PostingSort } from '../api';
import { PostingList } from '../components/PostingList';
import { ErrorBanner, Loading } from '../components/States';
import { useFilters } from '../filters';
import { num } from '../format';

const PAGE_SIZE = 25;
const SORTS: Record<PostingSort, string> = { newest: 'Newest first', match: 'Best match first', salary: 'Highest salary first' };

export function Postings() {
  const [filters] = useFilters();
  const [params, setParams] = useSearchParams();
  const q = params.get('q') ?? '';
  const skill = params.get('skill') ?? '';
  const company = params.get('company') ?? '';
  const sort = (Object.keys(SORTS) as PostingSort[]).find((s) => s === params.get('sort')) ?? 'newest';
  const page = Math.max(1, Number.parseInt(params.get('page') ?? '1', 10) || 1);
  const [draft, setDraft] = useState(q);
  const [seenQ, setSeenQ] = useState(q);
  if (q !== seenQ) { // the address changed under the box (back, forward, a link): show what is being searched
    setSeenQ(q);
    setDraft(q);
  }
  const meta = useQuery({ queryKey: ['meta'], queryFn: api.meta });
  const query = useQuery({
    queryKey: ['postings', filters, q, skill, company, sort, page],
    queryFn: () => api.postings(filters, {
      q: q || undefined, skill: skill || undefined, company: company || undefined, sort, page, pageSize: PAGE_SIZE,
    }),
    placeholderData: keepPreviousData,
  });

  // Any change except the page itself starts again from page 1.
  const set = (patch: Record<string, string>) =>
    setParams((current) => {
      const out = new URLSearchParams(current);
      Object.entries(patch).forEach(([key, value]) => (value ? out.set(key, value) : out.delete(key)));
      if (!('page' in patch)) out.delete('page');
      return out;
    });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    set({ q: draft.trim() });
  };

  const data = query.data;
  const pages = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1;
  // While the next search loads, `data` is still the previous one (keepPreviousData): its page count says nothing
  // about the page now in the address.
  const settled = data !== undefined && !query.isPlaceholderData;
  const shown = settled ? Math.min(page, pages) : page;
  useEffect(() => {
    // The results shrank while this page was open (a new run, a narrower filter): fall back to the last page.
    if (settled && page > pages) {
      setParams((current) => {
        const out = new URLSearchParams(current);
        if (pages > 1) out.set('page', String(pages));
        else out.delete('page');
        return out;
      }, { replace: true });
    }
  }, [settled, page, pages, setParams]);
  const companies = [...new Set([...(meta.data?.companies ?? []), ...(company ? [company] : [])])];

  return (
    <section className="section" aria-labelledby="h-postings">
      <h2 id="h-postings">Postings</h2>
      <form className="toolbar" role="search" onSubmit={submit}>
        <div className="field">
          <label className="field-label" htmlFor="postings-q">Title or company</label>
          <input id="postings-q" type="search" value={draft} placeholder="e.g. bank, platform" onChange={(e) => setDraft(e.target.value)} />
        </div>
        <button type="submit">Search</button>
        <div className="field">
          <label className="field-label" htmlFor="postings-company">Company</label>
          <select id="postings-company" value={company} onChange={(e) => set({ company: e.target.value })}>
            <option value="">All companies</option>
            {companies.map((c) => <option key={c} value={c}>{c}</option>)}
          </select>
        </div>
        <div className="field">
          <label className="field-label" htmlFor="postings-sort">Sort</label>
          <select id="postings-sort" value={sort} onChange={(e) => set({ sort: e.target.value === 'newest' ? '' : e.target.value })}>
            {(Object.keys(SORTS) as PostingSort[]).map((s) => <option key={s} value={s}>{SORTS[s]}</option>)}
          </select>
        </div>
      </form>

      {skill && (
        <ul className="pills" aria-label="Active filters">
          <li>
            <button type="button" aria-label={`Remove skill filter ${skill}`} onClick={() => set({ skill: '' })}>Skill: {skill} ×</button>
          </li>
        </ul>
      )}

      {query.error && <ErrorBanner error={query.error} />}
      {!data ? (
        !query.error && <Loading what="postings" />
      ) : (
        <>
          <p className="muted small">{num(data.total)} postings</p>
          <PostingList rows={data.items} caption="Postings" />
          {data.total > data.pageSize && (
            <nav className="pager" aria-label="Result pages">
              <button type="button" disabled={shown <= 1} onClick={() => set({ page: String(shown - 1) })}>Previous</button>
              <span className="num">Page {shown} of {pages}</span>
              <button type="button" disabled={shown >= pages} onClick={() => set({ page: String(shown + 1) })}>Next</button>
            </nav>
          )}
        </>
      )}
    </section>
  );
}
