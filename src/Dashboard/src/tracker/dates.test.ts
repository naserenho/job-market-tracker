import { describe, expect, it } from 'vitest';
import { dayText, localToday } from './dates';

describe('tracker dates', () => {
  it('writes the local calendar date', () => {
    expect(localToday(new Date(2027, 2, 1, 23, 30))).toBe('2027-03-01');
  });

  it('says Today for today and adds the year only when it differs', () => {
    expect(dayText('2027-03-01', '2027-03-01')).toBe('Today');
    expect(dayText('2026-12-24', '2027-01-02')).toContain('2026');
    expect(dayText('2027-03-08', '2027-03-01')).not.toContain('2027');
  });
});
