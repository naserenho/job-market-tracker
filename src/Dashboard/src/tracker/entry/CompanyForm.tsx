import type { ChangeEvent } from 'react';
import { tracker, type CompanyInput, type CompanyView } from '../api';
import { Field } from '../Field';
import { SaveError } from '../SaveError';
import { useEditableForm } from '../useEditableForm';
import { useTrackerMutation } from '../useTrackerMutation';

type Values = Omit<CompanyInput, 'version'>;

const TEXT_FIELDS: { key: 'name' | 'website' | 'officeLocation' | 'companySize' | 'annualLeave'; label: string; hint?: string }[] = [
  { key: 'name', label: 'Company name' },
  { key: 'website', label: 'Website' },
  { key: 'officeLocation', label: 'Office location' },
  { key: 'companySize', label: 'Company size' },
  { key: 'annualLeave', label: 'Annual leave', hint: 'e.g. 20 days + 5 personal' },
];

function valuesOf(company: CompanyView): Values {
  return {
    name: company.name, website: company.website, officeLocation: company.officeLocation, companySize: company.companySize,
    annualLeave: company.annualLeave, benefits: company.benefits, notes: company.notes,
  };
}

export function CompanyForm({ company }: { company: CompanyView }) {
  const editable = useEditableForm(valuesOf(company), company.version);
  const { form } = editable;
  const save = useTrackerMutation(
    (values: Values) => tracker.editCompany(company.id, { ...values, version: editable.version }),
    (_, values) => editable.saved(values),
  );
  const change = (key: keyof Values) => (e: ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) => editable.change(key, e.target.value);

  return (
    <form className="panel" aria-labelledby="h-company" onSubmit={(e) => { e.preventDefault(); save.mutate({ ...form }); }}>
      <h3 id="h-company">Company</h3>
      {company.applications > 1 && <p className="hint">Shared by all {company.applications} of your {company.name} entries.</p>}
      <div className="form-grid">
        {TEXT_FIELDS.map(({ key, label, hint }) => (
          <Field key={key} id={`company-${key}`} label={label} hint={hint}>
            <input id={`company-${key}`} type={key === 'website' ? 'url' : 'text'} value={form[key]} onChange={change(key)} />
          </Field>
        ))}
        <Field id="company-benefits" label="Benefits" wide>
          <textarea id="company-benefits" value={form.benefits} onChange={change('benefits')} />
        </Field>
        <Field id="company-notes" label="Company notes" wide>
          <textarea id="company-notes" value={form.notes} onChange={change('notes')} />
        </Field>
      </div>
      <div className="actions">
        <button type="submit" className="primary" disabled={save.isPending}>Save company</button>
      </div>
      <SaveError error={save.error} onReload={() => { editable.reload(); save.reset(); }} />
    </form>
  );
}
