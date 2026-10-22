import { useQueryClient } from '@tanstack/react-query';
import { ApiError } from '../api';

// A failed save, shown by the form that made it. On a version conflict (someone saved first, e.g. in another tab) it
// offers Reload: the form drops its unsaved changes (onReload) and the latest values are fetched. A conflict that names
// another record (`id`), such as a company name already in use, gets no Reload: only changing the input helps.
export function SaveError({ error, onReload }: { error: unknown; onReload?: () => void }) {
  const queryClient = useQueryClient();
  if (!error) return null;
  const message = error instanceof Error ? error.message : 'The change was not saved.';
  const conflict = error instanceof ApiError && error.status === 409 && typeof error.details.id !== 'string';
  const reload = () => {
    onReload?.();
    void queryClient.invalidateQueries({ queryKey: ['tracker'] });
  };
  return (
    <div className="banner" role="alert">
      {message}{' '}
      {conflict && <button type="button" onClick={reload}>Reload</button>}
    </div>
  );
}
