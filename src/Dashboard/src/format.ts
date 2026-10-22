export const FAMILY_LABELS: Record<string, string> = {
  Dotnet: '.NET',
  Fullstack: 'Full stack',
  SoftwareEngineer: 'Software engineer',
  LeadManager: 'Lead / manager',
  Other: 'Other',
};

export const SENIORITY_LABELS: Record<string, string> = {
  Unspecified: 'Not stated',
  Junior: 'Junior',
  Intermediate: 'Intermediate',
  Senior: 'Senior',
  StaffPrincipal: 'Staff / principal',
  Lead: 'Lead',
  Manager: 'Manager',
};

export function label(map: Record<string, string>, key: string): string {
  return map[key] ?? key;
}

// How the active region writes money, numbers and dates. App sets it once /api/meta has loaded; until then the
// defaults apply.
export interface DisplayRegion {
  culture: string;
  timeZone: string;
  moneyPrefix: string;
}

export const DEFAULT_DISPLAY: DisplayRegion = { culture: 'en-CA', timeZone: 'America/Toronto', moneyPrefix: '$' };

let display: DisplayRegion = DEFAULT_DISPLAY;

export function setDisplayRegion(region: DisplayRegion): void {
  display = { culture: region.culture, timeZone: region.timeZone, moneyPrefix: region.moneyPrefix };
}

export function displayCulture(): string {
  return display.culture;
}

export function num(value: number): string {
  return value.toLocaleString(display.culture);
}

// Amounts of 1,000 or more in thousands ($162k, AED 25k); smaller ones whole ($45).
export function money(value: number | null | undefined): string {
  if (value === null || value === undefined || Number.isNaN(value)) return '—';
  return value >= 1000 ? `${display.moneyPrefix}${Math.round(value / 1000)}k` : `${display.moneyPrefix}${Math.round(value)}`;
}

export function salaryText(min: number | null, max: number | null, period: string | null): string {
  if (min === null || max === null) return '—';
  const range = min === max ? money(min) : `${money(min)}–${money(max)}`;
  return period === 'Hour' ? `${range}/h` : period === 'Month' ? `${range}/mo` : range;
}

export function pct(share: number | null | undefined): string {
  if (share === null || share === undefined || Number.isNaN(share)) return '—';
  return `${Math.round(share * 100)}%`;
}

// Change in share between two windows, in percentage points.
export function signedPts(change: number | null): string {
  if (change === null || Number.isNaN(change)) return '—';
  const pts = Math.round(change * 100);
  if (pts === 0) return '±0 pts';
  return `${pts > 0 ? '+' : '−'}${Math.abs(pts)} pts`;
}

export function dateText(iso: string | null | undefined): string {
  if (!iso) return '—';
  return new Date(iso).toLocaleDateString(display.culture, { year: 'numeric', month: 'short', day: 'numeric', timeZone: display.timeZone });
}
