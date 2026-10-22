import { useState } from 'react';
import { tracker, type CvProposal } from '../api';
import { Field } from '../Field';
import { SaveError } from '../SaveError';
import { useTrackerMutation } from '../useTrackerMutation';

function thinText(textSource: CvProposal['textSource'], skillCount: number): string {
  if (textSource === 'entry') {
    return `Only ${skillCount} ${skillCount === 1 ? 'skill was' : 'skills were'} found in the job text you saved. If it isn't the whole posting, paste it again.`;
  }
  return textSource === 'market'
    ? "This entry only has a short snippet of the posting, so the match below is rough. Paste the whole posting for a better one. It's saved on this entry only."
    : "This entry has no job text yet. Paste the posting to see what it asks for. It's saved on this entry only.";
}

// The job text saved on this entry (never in the market data). Open when the text is thin; otherwise, once text is
// saved on the entry, a collapsed form to replace or clear it (a market entry then goes back to the collected posting).
export function PostingTextForm({ entryId, version, textSource, skillCount, thin }: {
  entryId: string;
  version: number;
  textSource: CvProposal['textSource'];
  skillCount: number;
  thin: boolean;
}) {
  const [text, setText] = useState('');
  const [message, setMessage] = useState('');
  const save = useTrackerMutation((postingText: string) => tracker.savePostingText(entryId, { postingText, version }), (_, postingText) => {
    setText('');
    setMessage(postingText ? 'Job text saved.' : 'Saved job text cleared.');
  });

  const form = (
    <form className="form-grid" onSubmit={(e) => {
      e.preventDefault();
      if (text.trim()) save.mutate(text);
    }}>
      <Field id="full-text" label="Full job text" wide>
        <textarea id="full-text" rows={8} value={text} onChange={(e) => { setText(e.target.value); setMessage(''); }} />
      </Field>
      <div className="actions wide">
        <button type="submit" className="primary" disabled={!text.trim() || save.isPending}>Save job text</button>
        {textSource === 'entry' && (
          <button type="button" disabled={save.isPending} onClick={() => save.mutate('')}>Clear the saved text</button>
        )}
      </div>
      {message && <p className="wide" role="status">{message}</p>}
      <div className="wide"><SaveError error={save.error} /></div>
    </form>
  );

  return thin ? (
    <section className="panel" aria-labelledby="h-posting-text">
      <h3 id="h-posting-text">Paste the full job text</h3>
      <p className="hint">{thinText(textSource, skillCount)}</p>
      {form}
    </section>
  ) : (
    <details className="panel">
      <summary>Replace the job text</summary>
      <p className="hint">The text saved on this entry is used instead of the collected posting. Clearing it takes a market entry back to the collected posting.</p>
      {form}
    </details>
  );
}
