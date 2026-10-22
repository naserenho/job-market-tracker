import { useState } from 'react';

function same<T extends Record<keyof T, string>>(a: T, b: T): boolean {
  return (Object.keys(a) as (keyof T)[]).every((key) => a[key] === b[key]);
}

// Form state that survives refetches of the entry it edits. A follow-up, a document or a status change also moves the
// entry's version, and the page refetches after every save, so:
// - with no unsaved changes, new server data replaces the form;
// - with unsaved changes, the form keeps them, and adopts the new version only when these fields are unchanged on the
//   server. Otherwise it keeps the old version, so Save gets the 409 and Reload (see ADR 0011).
export function useEditableForm<T extends Record<keyof T, string>>(server: T, serverVersion: number) {
  const [form, setForm] = useState<T>(server);
  const [baseline, setBaseline] = useState<T>(server);
  const [version, setVersion] = useState(serverVersion);
  const [seen, setSeen] = useState(serverVersion);

  if (serverVersion !== seen) {
    setSeen(serverVersion);
    if (same(form, baseline)) {
      setForm(server);
      setBaseline(server);
      setVersion(serverVersion);
    } else if (same(server, baseline)) {
      setVersion(serverVersion);
    }
  }

  return {
    form,
    version,
    change: (key: keyof T, value: string) => setForm({ ...form, [key]: value }),
    // After a successful save these values are what the server holds.
    saved: (values: T) => setBaseline(values),
    // Drop unsaved changes and take the latest server values.
    reload: () => {
      setForm(server);
      setBaseline(server);
      setVersion(serverVersion);
    },
  };
}
