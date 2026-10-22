import type { AppStatus } from './api';

export function StatusChip({ status }: { status: AppStatus }) {
  return <span className={`status status-${status}`}>{status}</span>;
}
