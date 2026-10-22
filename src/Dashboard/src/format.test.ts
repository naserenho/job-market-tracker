import { afterEach, describe, expect, it } from 'vitest';
import { DEFAULT_DISPLAY, dateText, money, num, pct, salaryText, setDisplayRegion, signedPts } from './format';

describe('format', () => {
  it('shows yearly money in thousands and hourly money in dollars', () => {
    expect(money(162_000)).toBe('$162k');
    expect(money(45)).toBe('$45');
    expect(money(null)).toBe('—');
  });

  it('writes salary ranges with the period', () => {
    expect(salaryText(120_000, 140_000, 'Year')).toBe('$120k–$140k');
    expect(salaryText(100_000, 100_000, 'Year')).toBe('$100k');
    expect(salaryText(45, 60, 'Hour')).toBe('$45–$60/h');
    expect(salaryText(null, null, null)).toBe('—');
  });

  it('writes shares and changes without NaN', () => {
    expect(pct(0.254)).toBe('25%');
    expect(pct(Number.NaN)).toBe('—');
    expect(signedPts(0.05)).toBe('+5 pts');
    expect(signedPts(-0.031)).toBe('−3 pts');
    expect(signedPts(0)).toBe('±0 pts');
    expect(signedPts(null)).toBe('—');
  });

  describe('in another region', () => {
    afterEach(() => setDisplayRegion(DEFAULT_DISPLAY));

    it('uses its currency, monthly periods, number format and time zone', () => {
      setDisplayRegion({ culture: 'en-AE', timeZone: 'Asia/Dubai', moneyPrefix: 'AED ' });

      expect(money(162_000)).toBe('AED 162k');
      expect(salaryText(25_000, 35_000, 'Month')).toBe('AED 25k–AED 35k/mo');
      expect(num(1234)).toBe((1234).toLocaleString('en-AE'));
      expect(dateText('2026-10-02T21:30:00Z')).toBe(new Date('2026-10-02T21:30:00Z').toLocaleDateString('en-AE', { year: 'numeric', month: 'short', day: 'numeric', timeZone: 'Asia/Dubai' }));
    });
  });
});
