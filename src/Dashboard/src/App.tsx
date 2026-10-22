import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef } from 'react';
import { NavLink, Route, Routes, useLocation } from 'react-router';
import { api } from './api';
import { FilterBar } from './components/FilterBar';
import { Empty, ErrorBanner, StaleBanner } from './components/States';
import { linkSearch, useFilters } from './filters';
import { dateText, setDisplayRegion } from './format';
import { Overview } from './pages/Overview';
import { Postings } from './pages/Postings';
import { Salaries } from './pages/Salaries';
import { SkillDetail } from './pages/SkillDetail';
import { Skills } from './pages/Skills';
import { tracker } from './tracker/api';
import { TailorPage } from './tracker/cv/TailorPage';
import { EntryPage } from './tracker/entry/EntryPage';
import { PasteJob } from './tracker/PasteJob';
import { TrackerPage } from './tracker/TrackerPage';

const PAGES = [
  { to: '/', label: 'Overview' },
  { to: '/skills', label: 'Skills' },
  { to: '/salaries', label: 'Salaries' },
  { to: '/postings', label: 'Postings' },
];

const NO_PAGE = <Empty>There's no page here. Pick one from the menu above.</Empty>;

export function App() {
  const [filters] = useFilters();
  const location = useLocation();
  const onTracker = location.pathname.startsWith('/tracker');
  const meta = useQuery({ queryKey: ['meta'], queryFn: api.meta, refetchInterval: 60_000 });
  const trackerStatus = useQuery({ queryKey: ['tracker', 'status'], queryFn: tracker.status, refetchInterval: 60_000 });
  const overdue = trackerStatus.data?.overdue ?? 0;
  const queryClient = useQueryClient();
  const runId = meta.data?.latestRunId ?? null;
  const region = meta.data?.region;
  if (region) setDisplayRegion(region);
  const shownRun = useRef<string | null>(null);

  // A new collection landed: refetch every page's data so the numbers match the run in the header.
  useEffect(() => {
    if (shownRun.current !== null && runId !== null && runId !== shownRun.current)
      void queryClient.invalidateQueries({ predicate: (q) => q.queryKey[0] !== 'meta' });
    if (runId !== null) shownRun.current = runId;
  }, [runId, queryClient]);
  const search = linkSearch(filters);

  return (
    <div className="shell">
      <a className="skip" href="#content">Skip to content</a>
      <header className="masthead">
        <h1>{region?.title ?? 'Job market'}</h1>
        {meta.data?.latestRunId && (
          <span className="run-line">
            Run {meta.data.latestRunId} · {dateText(meta.data.latestRunAt)} · {meta.data.runs} runs over {meta.data.historyDays} days
          </span>
        )}
      </header>
      <nav className="nav" aria-label="Pages">
        {PAGES.map((p) => (
          <NavLink key={p.to} to={{ pathname: p.to, search }} end={p.to === '/'}>{p.label}</NavLink>
        ))}
        <NavLink to="/tracker">
          Tracker
          {overdue > 0 && (
            <span className="badge"><span className="sr-only">, </span>{overdue}<span className="sr-only"> overdue</span></span>
          )}
        </NavLink>
      </nav>
      {meta.data && !onTracker && <FilterBar meta={meta.data} />}
      {/* Keyed by region so pages first drawn with the default format redraw in the region's. */}
      <main id="content" key={region?.name ?? 'default'}>
        {onTracker ? (
          <>
          {trackerStatus.data?.lastBackupError && (
            <div className="banner warn" role="status">
              {trackerStatus.data.lastBackupError} Your changes are saved in the database; the copy updates again with your next change.
            </div>
          )}
          <Routes>
            <Route path="/tracker" element={<TrackerPage />} />
            <Route path="/tracker/new" element={<PasteJob />} />
            <Route path="/tracker/:id" element={<EntryPage />} />
            <Route path="/tracker/:id/cv" element={<TailorPage />} />
            <Route path="*" element={NO_PAGE} />
          </Routes>
          </>
        ) : (
          <>
            {meta.error && <ErrorBanner error={meta.error} />}
            {meta.data?.stale && <StaleBanner />}
            {meta.data && meta.data.runs === 0 && <Empty>No data yet: the first collection runs Tuesday or Friday at 07:00.</Empty>}
            {meta.data && meta.data.runs > 0 && (
              <Routes>
                <Route path="/" element={<Overview />} />
                <Route path="/skills" element={<Skills />} />
                <Route path="/skills/:name" element={<SkillDetail />} />
                <Route path="/salaries" element={<Salaries />} />
                <Route path="/postings" element={<Postings />} />
                <Route path="*" element={NO_PAGE} />
              </Routes>
            )}
          </>
        )}
      </main>
      <footer className="muted">
        Jobs data partly by <a href="https://www.adzuna.ca" target="_blank" rel="noreferrer">Adzuna</a>. Other postings come from company job boards, the Job Bank and saved pastes.
      </footer>
    </div>
  );
}
