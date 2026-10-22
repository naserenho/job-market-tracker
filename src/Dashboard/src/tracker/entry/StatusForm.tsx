import { useState } from 'react';
import { STATUSES, tracker, type AppStatus, type ApplicationDetail } from '../api';
import { localToday } from '../dates';
import { Field } from '../Field';
import { SaveError } from '../SaveError';
import { useTrackerMutation } from '../useTrackerMutation';

export function StatusForm({ entry }: { entry: ApplicationDetail }) {
  const [status, setStatus] = useState<AppStatus>(entry.status);
  const [on, setOn] = useState(localToday());
  const [note, setNote] = useState('');
  const save = useTrackerMutation(() => tracker.setStatus(entry.id, { status, on: on || null, note }));
  const unchanged = status === entry.status;

  return (
    <form className="panel" aria-labelledby="h-status" onSubmit={(e) => { e.preventDefault(); save.mutate(undefined); }}>
      <h3 id="h-status">Update status</h3>
      <div className="form-grid">
        <Field id="status-to" label="New status">
          <select id="status-to" value={status} onChange={(e) => setStatus(e.target.value as AppStatus)}>
            {STATUSES.map((s) => <option key={s} value={s}>{s}</option>)}
          </select>
        </Field>
        <Field id="status-on" label="On">
          <input id="status-on" type="date" value={on} max={localToday()} onChange={(e) => setOn(e.target.value)} />
        </Field>
        <Field id="status-note" label="Note" wide>
          <input id="status-note" type="text" value={note} onChange={(e) => setNote(e.target.value)} placeholder="e.g. Applied through Workday" />
        </Field>
      </div>
      <div className="actions">
        <button type="submit" className="primary" disabled={save.isPending || unchanged}>Update status</button>
        {unchanged && <span className="hint">Pick a different status to record a change.</span>}
      </div>
      <SaveError error={save.error} />
    </form>
  );
}
