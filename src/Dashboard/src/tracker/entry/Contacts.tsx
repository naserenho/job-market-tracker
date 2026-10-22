import { useState, type ChangeEvent } from 'react';
import { tracker, type ApplicationDetail, type ContactInput } from '../api';
import { Field } from '../Field';
import { SaveError } from '../SaveError';
import { useTrackerMutation } from '../useTrackerMutation';

const BLANK: ContactInput = { name: '', role: '', email: '', phone: '', linkedIn: '', notes: '' };

export function Contacts({ entry }: { entry: ApplicationDetail }) {
  const [form, setForm] = useState<ContactInput>(BLANK);
  const add = useTrackerMutation(() => tracker.addContact(entry.id, form), () => setForm(BLANK));
  const remove = useTrackerMutation((contactId: string) => tracker.deleteContact(entry.id, contactId));
  const change = (key: keyof ContactInput) => (e: ChangeEvent<HTMLInputElement>) => setForm({ ...form, [key]: e.target.value });

  return (
    <section className="panel" aria-labelledby="h-contacts">
      <h3 id="h-contacts">Contacts</h3>
      {entry.contacts.length === 0 ? (
        <p className="empty">No contacts yet.</p>
      ) : (
        <ul className="list">
          {entry.contacts.map((c) => (
            <li key={c.id}>
              <strong>{c.name}</strong>
              {c.role && <span className="muted">{c.role}</span>}
              {c.email && <span>{c.email}</span>}
              {c.phone && <span>{c.phone}</span>}
              {c.linkedIn && <a href={c.linkedIn} target="_blank" rel="noreferrer">LinkedIn ↗</a>}
              <button type="button" onClick={() => remove.mutate(c.id)} aria-label={`Delete contact ${c.name}`}>Delete</button>
            </li>
          ))}
        </ul>
      )}
      <form className="form-grid" onSubmit={(e) => { e.preventDefault(); if (form.name.trim()) add.mutate(undefined); }}>
        <Field id="contact-name" label="Name"><input id="contact-name" type="text" value={form.name} onChange={change('name')} /></Field>
        <Field id="contact-role" label="Role"><input id="contact-role" type="text" value={form.role} onChange={change('role')} placeholder="Recruiter, hiring manager…" /></Field>
        <Field id="contact-email" label="Email"><input id="contact-email" type="email" value={form.email} onChange={change('email')} /></Field>
        <Field id="contact-phone" label="Phone"><input id="contact-phone" type="text" value={form.phone} onChange={change('phone')} /></Field>
        <Field id="contact-linkedIn" label="LinkedIn"><input id="contact-linkedIn" type="url" value={form.linkedIn} onChange={change('linkedIn')} placeholder="https://www.linkedin.com/in/…" /></Field>
        <div className="actions"><button type="submit" className="primary" disabled={!form.name.trim() || add.isPending}>Add contact</button></div>
      </form>
      <SaveError error={add.error ?? remove.error} />
    </section>
  );
}
