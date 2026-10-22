import { useCallback, useMemo } from 'react';
import { useSearchParams } from 'react-router';

export type Where = 'all' | 'local' | 'remote';
export type TimeWindow = '30' | '60' | '90' | 'all';

export interface Filters {
  families: string[]; // RoleFamily names; empty = the profile's target families (the API's default)
  seniority: string[]; // Seniority names; empty = all
  where: Where;
  window: TimeWindow;
}

export const DEFAULT_FILTERS: Filters = { families: [], seniority: [], where: 'all', window: '30' };

const WHERES: readonly string[] = ['all', 'local', 'remote'];
const WINDOWS: readonly string[] = ['30', '60', '90', 'all'];
const FILTER_KEYS = ['families', 'seniority', 'where', 'window'];

function list(value: string | null): string[] {
  return (value ?? '').split(',').map((v) => v.trim()).filter((v) => v.length > 0);
}

// Unknown where/window values fall back to the defaults; unknown families or seniorities go to the API, which names them in a 400.
export function readFilters(params: URLSearchParams): Filters {
  const raw = params.get('where') ?? '';
  const where = raw.toLowerCase() === 'gta' ? 'local' : raw; // what links said before regions existed
  const win = params.get('window') ?? '';
  return {
    families: list(params.get('families')),
    seniority: list(params.get('seniority')),
    where: WHERES.includes(where) ? (where as Where) : DEFAULT_FILTERS.where,
    window: WINDOWS.includes(win) ? (win as TimeWindow) : DEFAULT_FILTERS.window,
  };
}

// Writes the filters into a copy of `params`, leaving other keys (q, sort, page…) alone and leaving defaults out.
export function writeFilters(filters: Filters, params: URLSearchParams = new URLSearchParams()): URLSearchParams {
  const out = new URLSearchParams(params);
  FILTER_KEYS.forEach((key) => out.delete(key));
  if (filters.families.length > 0) out.set('families', filters.families.join(','));
  if (filters.seniority.length > 0) out.set('seniority', filters.seniority.join(','));
  if (filters.where !== DEFAULT_FILTERS.where) out.set('where', filters.where);
  if (filters.window !== DEFAULT_FILTERS.window) out.set('window', filters.window);
  return out;
}

// Query string for an API call: the filters plus any extra parameters that have a value.
export function apiQuery(filters: Filters, extra: Record<string, string | number | undefined> = {}): string {
  const params = writeFilters(filters);
  Object.entries(extra).forEach(([key, value]) => {
    if (value !== undefined && value !== '') params.set(key, String(value));
  });
  const text = params.toString();
  return text ? `?${text}` : '';
}

// Search string for an in-app link: the current filters plus page-specific parameters.
export function linkSearch(filters: Filters, extra: Record<string, string> = {}): string {
  const params = writeFilters(filters);
  Object.entries(extra).forEach(([key, value]) => params.set(key, value));
  return params.toString();
}

export function useFilters(): [Filters, (next: Filters) => void] {
  const [params, setParams] = useSearchParams();
  const filters = useMemo(() => readFilters(params), [params]);
  const setFilters = useCallback(
    (next: Filters) =>
      setParams((current) => {
        const out = writeFilters(next, current);
        out.delete('page'); // new filters start again from the first page
        return out;
      }),
    [setParams],
  );
  return [filters, setFilters];
}
