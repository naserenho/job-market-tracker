import { useState } from 'react';
import { useNavigate } from 'react-router';
import { tracker, type ApplicationDetail } from '../api';
import { SaveError } from '../SaveError';
import { useTrackerMutation } from '../useTrackerMutation';

// The confirmation step lives in the page: browser confirm dialogs are easy to click through.
export function DeleteEntry({ entry }: { entry: ApplicationDetail }) {
  const navigate = useNavigate();
  const [asking, setAsking] = useState(false);
  const remove = useTrackerMutation(() => tracker.remove(entry.id), () => navigate('/tracker'));

  return (
    <section className="panel" aria-labelledby="h-delete">
      <h3 id="h-delete">Delete</h3>
      {!asking ? (
        <div className="actions"><button type="button" onClick={() => setAsking(true)}>Delete this entry</button></div>
      ) : (
        <div className="actions" role="group" aria-label="Confirm delete">
          <span>This removes the entry, its timeline, follow-ups, contacts and uploaded files. The company stays.</span>
          <button type="button" className="danger" onClick={() => remove.mutate(undefined)} disabled={remove.isPending}>Delete for good</button>
          <button type="button" onClick={() => setAsking(false)}>Keep it</button>
        </div>
      )}
      <SaveError error={remove.error} />
    </section>
  );
}
