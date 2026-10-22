import type { ChangeEvent } from 'react';
import { WORK_MODELS, tracker, type ApplicationDetail, type DetailsInput, type WorkModel } from '../api';
import { Field } from '../Field';
import { SaveError } from '../SaveError';
import { useEditableForm } from '../useEditableForm';
import { useTrackerMutation } from '../useTrackerMutation';

type Values = Omit<DetailsInput, 'version'>;
type TextKey = Exclude<keyof Values, 'workModel' | 'notes'>;

const TEXT_FIELDS: { key: TextKey; label: string; hint?: string }[] = [
  { key: 'title', label: 'Title' },
  { key: 'url', label: 'Link' },
  { key: 'location', label: 'Location' },
  { key: 'foundVia', label: 'How you found it', hint: 'Referral, LinkedIn, company site…' },
  { key: 'officeDays', label: 'Office days', hint: 'e.g. 3 days a week' },
  { key: 'teamSize', label: 'Team size' },
  { key: 'workingHours', label: 'Working hours' },
  { key: 'salaryPosted', label: 'Salary posted' },
  { key: 'salaryAsked', label: 'Salary you asked' },
  { key: 'salaryOffered', label: 'Salary offered' },
];

const WORK_MODEL_LABELS: Record<WorkModel, string> = { Unknown: 'Not known', Remote: 'Remote', Hybrid: 'Hybrid', Onsite: 'On-site' };

function valuesOf(entry: ApplicationDetail): Values {
  return {
    title: entry.title, url: entry.url, location: entry.location, foundVia: entry.foundVia, workModel: entry.workModel,
    officeDays: entry.officeDays, teamSize: entry.teamSize, workingHours: entry.workingHours, salaryPosted: entry.salaryPosted,
    salaryAsked: entry.salaryAsked, salaryOffered: entry.salaryOffered, notes: entry.notes,
  };
}

export function DetailsForm({ entry }: { entry: ApplicationDetail }) {
  const editable = useEditableForm(valuesOf(entry), entry.version);
  const { form } = editable;
  const save = useTrackerMutation(
    (values: Values) => tracker.editDetails(entry.id, { ...values, version: editable.version }),
    (_, values) => editable.saved(values),
  );
  const change = (key: keyof Values) => (e: ChangeEvent<HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement>) =>
    editable.change(key, e.target.value);

  return (
    <form className="panel" aria-labelledby="h-details" onSubmit={(e) => { e.preventDefault(); save.mutate({ ...form }); }}>
      <h3 id="h-details">Job details</h3>
      <div className="form-grid">
        {TEXT_FIELDS.map(({ key, label, hint }) => (
          <Field key={key} id={`details-${key}`} label={label} hint={hint}>
            <input id={`details-${key}`} type={key === 'url' ? 'url' : 'text'} value={form[key]} onChange={change(key)} />
          </Field>
        ))}
        <Field id="details-workModel" label="Work model">
          <select id="details-workModel" value={form.workModel} onChange={change('workModel')}>
            {WORK_MODELS.map((m) => <option key={m} value={m}>{WORK_MODEL_LABELS[m]}</option>)}
          </select>
        </Field>
        <Field id="details-notes" label="Notes" wide>
          <textarea id="details-notes" value={form.notes} onChange={change('notes')} />
        </Field>
      </div>
      <div className="actions">
        <button type="submit" className="primary" disabled={save.isPending}>Save details</button>
      </div>
      <SaveError error={save.error} onReload={() => { editable.reload(); save.reset(); }} />
    </form>
  );
}
