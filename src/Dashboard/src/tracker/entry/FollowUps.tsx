import { useState } from 'react';
import { tracker, type ApplicationDetail, type FollowUpView } from '../api';
import { dayText, localToday } from '../dates';
import { Field } from '../Field';
import { SaveError } from '../SaveError';
import { useTrackerMutation } from '../useTrackerMutation';

export function FollowUps({ entry }: { entry: ApplicationDetail }) {
  const [action, setAction] = useState('');
  const [dueOn, setDueOn] = useState('');
  const add = useTrackerMutation(() => tracker.addFollowUp(entry.id, { action: action.trim(), dueOn, doneOn: null }), () => {
    setAction('');
    setDueOn('');
  });
  const complete = useTrackerMutation((f: FollowUpView) =>
    tracker.editFollowUp(entry.id, f.id, { action: f.action, dueOn: f.dueOn, doneOn: localToday() }));
  const remove = useTrackerMutation((f: FollowUpView) => tracker.deleteFollowUp(entry.id, f.id));
  const today = localToday();

  return (
    <section className="panel" aria-labelledby="h-followups">
      <h3 id="h-followups">Follow-ups</h3>
      {entry.followUps.length === 0 ? (
        <p className="empty">No follow-ups yet.</p>
      ) : (
        <ul className="list">
          {entry.followUps.map((f) => (
            <li key={f.id}>
              <span className={f.doneOn ? 'done' : f.dueOn < today ? 'overdue' : undefined}>{dayText(f.dueOn)} · {f.action}</span>
              {!f.doneOn && (
                <button type="button" onClick={() => complete.mutate(f)} aria-label={`Mark "${f.action}" done`}>Done</button>
              )}
              <button type="button" onClick={() => remove.mutate(f)} aria-label={`Delete "${f.action}"`}>Delete</button>
            </li>
          ))}
        </ul>
      )}
      <form className="form-grid" onSubmit={(e) => { e.preventDefault(); if (action.trim() && dueOn) add.mutate(undefined); }}>
        <Field id="followup-action" label="Next action">
          <input id="followup-action" type="text" value={action} onChange={(e) => setAction(e.target.value)} placeholder="e.g. Email the recruiter" />
        </Field>
        <Field id="followup-due" label="Due">
          <input id="followup-due" type="date" value={dueOn} onChange={(e) => setDueOn(e.target.value)} />
        </Field>
        <div className="actions">
          <button type="submit" className="primary" disabled={!action.trim() || !dueOn || add.isPending}>Add follow-up</button>
        </div>
      </form>
      <SaveError error={add.error ?? complete.error ?? remove.error} />
    </section>
  );
}
