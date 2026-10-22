import { displayCulture } from '../format';

// The API stores dates as yyyy-MM-dd in this PC's calendar.
export function localToday(now: Date = new Date()): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
}

export function dayText(iso: string, today: string = localToday()): string {
  if (iso === today) return 'Today';
  const [year, month, day] = iso.split('-').map(Number);
  const sameYear = iso.slice(0, 4) === today.slice(0, 4);
  return new Date(year, month - 1, day).toLocaleDateString(displayCulture(),
    sameYear ? { month: 'short', day: 'numeric' } : { month: 'short', day: 'numeric', year: 'numeric' });
}
