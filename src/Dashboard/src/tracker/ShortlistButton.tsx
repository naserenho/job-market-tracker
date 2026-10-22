import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router';
import { ApiError } from '../api';
import { tracker } from './api';
import { useTrackerMutation } from './useTrackerMutation';

// "Shortlist" on a market posting. Once tracked, it becomes the entry's status and links to it. It hides itself when
// the tracker can't be reached, so market pages keep working without it.
export function ShortlistButton({ postingId, title }: { postingId: string; title: string }) {
  const index = useQuery({ queryKey: ['tracker', 'index'], queryFn: tracker.index, staleTime: 30_000 });
  const add = useTrackerMutation(() => tracker.shortlist(postingId));
  if (index.isError || !postingId) return null;

  const tracked = index.data?.[postingId];
  const existing = add.error instanceof ApiError && add.error.status === 409 && typeof add.error.details.id === 'string'
    ? add.error.details.id
    : null;
  const id = tracked?.id ?? add.data?.id ?? existing;
  if (id) {
    const status = tracked?.status ?? 'Shortlisted';
    return (
      <Link className={`status status-${status}`} to={`/tracker/${id}`} aria-label={`${status}: open "${title}" in the tracker`}>
        {status}
      </Link>
    );
  }
  return (
    <>
      <button type="button" className="small-button" onClick={() => add.mutate(undefined)} disabled={index.isPending || add.isPending}
        aria-label={`Shortlist "${title}"`}>
        {add.isPending ? 'Saving…' : 'Shortlist'}
      </button>
      {add.error && <span className="field-error" role="alert">{add.error.message}</span>}
    </>
  );
}
