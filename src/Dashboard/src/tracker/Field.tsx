import type { ReactNode } from 'react';

// A labelled control with its hint and error. Pass the control as children, with the same id.
export function Field({ id, label, error, hint, wide, children }: {
  id: string;
  label: string;
  error?: string;
  hint?: string;
  wide?: boolean;
  children: ReactNode;
}) {
  return (
    <div className={wide ? 'field wide' : 'field'}>
      <label className="field-label" htmlFor={id}>{label}</label>
      {children}
      {hint && <span className="hint">{hint}</span>}
      {error && <span className="field-error" id={`${id}-error`}>{error}</span>}
    </div>
  );
}
