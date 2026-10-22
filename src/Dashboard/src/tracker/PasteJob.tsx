import { useQuery } from '@tanstack/react-query';
import { useState, type ChangeEvent, type FormEvent } from 'react';
import { Link, useNavigate } from 'react-router';
import { api, ApiError } from '../api';
import { ErrorBanner } from '../components/States';
import { tracker, type NewEntry, type PasteInput } from './api';
import { Field } from './Field';
import { useTrackerMutation } from './useTrackerMutation';

type Errors = Partial<Record<keyof PasteInput, string>>;

const EMPTY: PasteInput = { title: '', company: '', url: '', location: '', postingText: '', status: 'Shortlisted' };

export function validatePaste(input: PasteInput): Errors {
  const errors: Errors = {};
  if (!input.title.trim()) errors.title = 'Title is required.';
  if (!input.company.trim()) errors.company = 'Company is required.';
  if (!input.postingText.trim()) errors.postingText = 'Paste the job text: the skill match and market stats need it.';
  if (input.url.trim() && !/^https?:\/\//i.test(input.url.trim())) errors.url = 'Use a full web address starting with https://.';
  return errors;
}

export function PasteJob() {
  const navigate = useNavigate();
  const companies = useQuery({ queryKey: ['tracker', 'companies'], queryFn: tracker.companies });
  const region = useQuery({ queryKey: ['meta'], queryFn: api.meta }).data?.region;
  const [form, setForm] = useState<PasteInput>(EMPTY);
  const [errors, setErrors] = useState<Errors>({});
  const save = useTrackerMutation((input: PasteInput) => tracker.paste(input), (created: NewEntry) => navigate(`/tracker/${created.id}`));
  const fieldError = save.error instanceof ApiError && typeof save.error.details.field === 'string' ? save.error.details.field : null;
  const shown: Errors = fieldError && fieldError in EMPTY && save.error ? { ...errors, [fieldError]: save.error.message } : errors;

  const change = (key: keyof PasteInput) => (e: ChangeEvent<HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement>) =>
    setForm({ ...form, [key]: e.target.value });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    const found = validatePaste(form);
    setErrors(found);
    if (Object.keys(found).length === 0) save.mutate(form);
  };

  const invalid = (key: keyof PasteInput) => (shown[key] ? { 'aria-invalid': true, 'aria-describedby': `paste-${key}-error` } : {});

  return (
    <section className="section" aria-labelledby="h-paste">
      <p className="eyebrow"><Link to="/tracker">Tracker</Link> / Paste a job</p>
      <h2 id="h-paste">Paste a job</h2>
      <p className="muted small">
        For jobs the collector doesn't see (LinkedIn, bank career sites, referrals). The job is tracked now, and the next
        Tuesday or Friday rebuild adds it to the market data too.
      </p>
      <form className="form-grid" onSubmit={submit} noValidate>
        <Field id="paste-title" label="Title" error={shown.title}>
          <input id="paste-title" type="text" value={form.title} onChange={change('title')} {...invalid('title')} />
        </Field>
        <Field id="paste-company" label="Company" error={shown.company}>
          <input id="paste-company" type="text" list="paste-companies" value={form.company} onChange={change('company')} {...invalid('company')} />
          <datalist id="paste-companies">{companies.data?.map((c) => <option key={c.id} value={c.name} />)}</datalist>
        </Field>
        <Field id="paste-url" label="Link" error={shown.url}>
          <input id="paste-url" type="url" value={form.url} onChange={change('url')} placeholder="https://" {...invalid('url')} />
        </Field>
        <Field id="paste-location" label="Location" hint={region ? `e.g. ${region.localLabel} or ${region.remoteLabel}` : 'e.g. a city, or Remote'}>
          <input id="paste-location" type="text" value={form.location} onChange={change('location')} />
        </Field>
        <Field id="paste-status" label="Starting status">
          <select id="paste-status" value={form.status} onChange={change('status')}>
            <option value="Shortlisted">Shortlisted</option>
            <option value="Applied">Applied</option>
          </select>
        </Field>
        <Field id="paste-postingText" label="Job text" error={shown.postingText} wide>
          <textarea id="paste-postingText" rows={12} value={form.postingText} onChange={change('postingText')} {...invalid('postingText')} />
        </Field>
        <div className="actions wide">
          <button type="submit" className="primary" disabled={save.isPending}>{save.isPending ? 'Saving…' : 'Save to tracker'}</button>
          <Link to="/tracker">Cancel</Link>
        </div>
      </form>
      {save.error && !fieldError && <ErrorBanner error={save.error} />}
    </section>
  );
}
