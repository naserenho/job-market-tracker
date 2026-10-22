import { useState } from 'react';
import { Link } from 'react-router';
import { DOCUMENT_KINDS, tracker, type ApplicationDetail, type DocumentKind } from '../api';
import { dayText, localToday } from '../dates';
import { Field } from '../Field';
import { SaveError } from '../SaveError';
import { useTrackerMutation } from '../useTrackerMutation';

const MAX_BYTES = 10 * 1024 * 1024;

function sizeText(bytes: number): string {
  return bytes >= 1024 * 1024 ? `${(bytes / 1024 / 1024).toFixed(1)} MB` : `${Math.max(1, Math.round(bytes / 1024))} KB`;
}

export function Documents({ entry }: { entry: ApplicationDetail }) {
  const [file, setFile] = useState<File | null>(null);
  const [kind, setKind] = useState<DocumentKind>('Cv');
  const [label, setLabel] = useState('');
  const [sentOn, setSentOn] = useState(localToday());
  const [inputKey, setInputKey] = useState(0);
  const [fileError, setFileError] = useState('');
  const upload = useTrackerMutation((chosen: File) => {
    const form = new FormData();
    form.append('file', chosen);
    form.append('kind', kind);
    form.append('label', label);
    form.append('sentOn', sentOn);
    return tracker.upload(entry.id, form);
  }, () => {
    setFile(null);
    setLabel('');
    setInputKey((k) => k + 1); // clears the file input
  });
  const remove = useTrackerMutation((documentId: string) => tracker.deleteDocument(documentId));

  return (
    <section className="panel" aria-labelledby="h-documents">
      <div className="tracker-head">
        <h3 id="h-documents">Documents</h3>
        <Link className="button" to={`/tracker/${entry.id}/cv`}>Tailor CV</Link>
      </div>
      {entry.documents.length === 0 ? (
        <p className="empty">No documents yet.</p>
      ) : (
        <ul className="list">
          {entry.documents.map((d) => (
            <li key={d.id}>
              <a href={tracker.documentUrl(d.id)}>{d.label}</a>
              {d.generated && <span className="status">generated</span>}
              <span className="muted small">
                {DOCUMENT_KINDS.find((k) => k.kind === d.kind)?.label} · {d.generated ? 'generated' : 'sent'} {dayText(d.sentOn)} · {sizeText(d.sizeBytes)}
              </span>
              <button type="button" onClick={() => remove.mutate(d.id)} aria-label={`Delete document ${d.label}`}>Delete</button>
            </li>
          ))}
        </ul>
      )}
      <form className="form-grid" onSubmit={(e) => {
        e.preventDefault();
        if (!file) return;
        // Checked here first: a large file would otherwise be sent in full before the API could refuse it.
        if (file.size > MAX_BYTES) setFileError('Files can be up to 10 MB.');
        else upload.mutate(file);
      }}>
        <Field id="doc-file" label="File" hint="PDF, DOCX, DOC, TXT or MD, up to 10 MB" error={fileError}>
          <input key={inputKey} id="doc-file" type="file" accept=".pdf,.docx,.doc,.txt,.md"
            onChange={(e) => { setFile(e.target.files?.[0] ?? null); setFileError(''); }} />
        </Field>
        <Field id="doc-kind" label="Kind">
          <select id="doc-kind" value={kind} onChange={(e) => setKind(e.target.value as DocumentKind)}>
            {DOCUMENT_KINDS.map((k) => <option key={k.kind} value={k.kind}>{k.label}</option>)}
          </select>
        </Field>
        <Field id="doc-label" label="Label" hint="Defaults to the file name">
          <input id="doc-label" type="text" value={label} onChange={(e) => setLabel(e.target.value)} />
        </Field>
        <Field id="doc-sent" label="Sent on">
          <input id="doc-sent" type="date" value={sentOn} onChange={(e) => setSentOn(e.target.value)} />
        </Field>
        <div className="actions"><button type="submit" className="primary" disabled={!file || upload.isPending}>Upload</button></div>
      </form>
      <SaveError error={upload.error ?? remove.error} />
    </section>
  );
}
