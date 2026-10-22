import { describe, expect, it } from 'vitest';
import { DEFAULT_FILTERS, apiQuery, linkSearch, readFilters, writeFilters, type Filters } from './filters';

describe('filters in the URL', () => {
  it('reads the defaults from an empty query', () => {
    expect(readFilters(new URLSearchParams())).toEqual(DEFAULT_FILTERS);
  });

  it('round-trips every filter', () => {
    const filters: Filters = { families: ['Dotnet', 'LeadManager'], seniority: ['Senior', 'Lead'], where: 'local', window: '90' };
    expect(readFilters(writeFilters(filters))).toEqual(filters);
  });

  it('leaves defaults out and keeps unrelated keys', () => {
    const params = writeFilters(DEFAULT_FILTERS, new URLSearchParams('q=bank&families=Other&window=60'));
    expect(params.toString()).toBe('q=bank');
  });

  it('falls back to the defaults for place and window values the API would reject', () => {
    const f = readFilters(new URLSearchParams('where=moon&window=45'));
    expect([f.where, f.window]).toEqual(['all', '30']);
  });

  it('builds API query strings, skipping empty extras', () => {
    expect(apiQuery({ ...DEFAULT_FILTERS, where: 'remote' }, { q: 'bank', skill: undefined, company: '', page: 2 })).toBe('?where=remote&q=bank&page=2');
    expect(apiQuery(DEFAULT_FILTERS)).toBe('');
  });

  it('builds link search strings that carry the filters', () => {
    expect(linkSearch({ ...DEFAULT_FILTERS, window: 'all' }, { company: 'Acme' })).toBe('window=all&company=Acme');
  });

  it('reads where=gta from links made before regions as local', () => {
    expect(readFilters(new URLSearchParams('where=gta')).where).toBe('local');
    expect(readFilters(new URLSearchParams('where=local')).where).toBe('local');
  });
});
