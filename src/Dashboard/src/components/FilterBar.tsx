import type { Meta } from '../api';
import { useFilters, type Filters, type TimeWindow, type Where } from '../filters';
import { FAMILY_LABELS, SENIORITY_LABELS, label } from '../format';

const WINDOW_LABELS: Record<TimeWindow, string> = { '30': 'Last 30 days', '60': 'Last 60 days', '90': 'Last 90 days', all: 'All history' };

function toggle(list: string[], value: string, on: boolean): string[] {
  return on ? [...list, value] : list.filter((v) => v !== value);
}

export function FilterBar({ meta }: { meta: Meta }) {
  const [filters, setFilters] = useFilters();
  // No families in the URL means the profile's target families (the API's default), so those show as ticked.
  const families = filters.families.length > 0 ? filters.families : meta.targetFamilies;
  const update = (patch: Partial<Filters>) => setFilters({ ...filters, ...patch });
  const { localLabel, remoteLabel } = meta.region;
  const whereLabels: Record<Where, string> = { all: `${localLabel} + ${remoteLabel}`, local: `${localLabel} only`, remote: `${remoteLabel} only` };
  const senioritySummary = filters.seniority.length === 0
    ? 'Seniority: all'
    : `Seniority: ${filters.seniority.map((s) => label(SENIORITY_LABELS, s)).join(', ')}`;

  return (
    <form className="filters" aria-label="Filters" onSubmit={(e) => e.preventDefault()}>
      <fieldset className="field">
        <legend className="field-label">Roles</legend>
        <div className="checks">
          {meta.families.map((f) => (
            <label key={f}>
              <input
                type="checkbox"
                checked={families.includes(f)}
                disabled={families.length === 1 && families.includes(f)}
                onChange={(e) => update({ families: toggle(families, f, e.target.checked) })}
              />
              {label(FAMILY_LABELS, f)}
            </label>
          ))}
        </div>
      </fieldset>

      <details className="multi field">
        <summary>{senioritySummary}</summary>
        <fieldset className="checks">
          <legend className="sr-only">Seniority</legend>
          {meta.seniorities.map((s) => (
            <label key={s}>
              <input
                type="checkbox"
                checked={filters.seniority.includes(s)}
                onChange={(e) => update({ seniority: toggle(filters.seniority, s, e.target.checked) })}
              />
              {label(SENIORITY_LABELS, s)}
            </label>
          ))}
        </fieldset>
      </details>

      <div className="field">
        <label className="field-label" htmlFor="filter-where">Where</label>
        <select id="filter-where" value={filters.where} onChange={(e) => update({ where: e.target.value as Where })}>
          {(Object.keys(whereLabels) as Where[]).map((w) => <option key={w} value={w}>{whereLabels[w]}</option>)}
        </select>
      </div>

      <div className="field">
        <label className="field-label" htmlFor="filter-window">Window</label>
        <select id="filter-window" value={filters.window} onChange={(e) => update({ window: e.target.value as TimeWindow })}>
          {(Object.keys(WINDOW_LABELS) as TimeWindow[]).map((w) => <option key={w} value={w}>{WINDOW_LABELS[w]}</option>)}
        </select>
      </div>
    </form>
  );
}
