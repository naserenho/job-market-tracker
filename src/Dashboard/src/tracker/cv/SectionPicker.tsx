import type { CvChoices, ProposalSection } from '../api';
import { chosenIn } from './tailor';

function firstWords(text: string): string {
  const parts = text.split(/\s+/).filter(Boolean);
  return parts.slice(0, 4).join(' ') + (parts.length > 4 ? '…' : '');
}

// One role or project: ticked bullets first, in print order with ↑/↓, then the unticked ones in file order.
export function SectionPicker({ section, choices, wanted, highlighted, onToggle, onMove }: {
  section: ProposalSection;
  choices: CvChoices;
  wanted: Set<string>;
  highlighted: Set<string>;
  onToggle: (bulletId: string) => void;
  onMove: (bulletId: string, delta: -1 | 1) => void;
}) {
  const chosen = chosenIn(section, choices);
  const order = new Map(chosen.map((b, i) => [b.id, i]));
  const rest = section.bullets.filter((b) => !order.has(b.id));
  const over = chosen.length > section.maxBullets;
  return (
    <fieldset className="panel picker">
      <legend>
        <strong>{section.company ? `${section.title} — ${section.company}` : section.title}</strong>{' '}
        <span className="muted small">{[section.location, section.dates].filter(Boolean).join(' | ')}</span>
      </legend>
      <p className={over ? 'field-error' : 'hint'}>
        {chosen.length} of {section.maxBullets} bullets{over ? ': more than cv.json suggests for this role. Fine, but keep the CV tight.' : ''}
      </p>
      <ul className="bullets">
        {[...chosen, ...rest].map((b) => {
          const at = order.get(b.id);
          return (
            <li key={b.id} className={highlighted.has(b.id) ? 'highlight' : undefined}>
              <label className="bullet-pick">
                <input type="checkbox" checked={at !== undefined} onChange={() => onToggle(b.id)} />
                <span>{b.text}</span>
              </label>
              <span className="skill-chips">
                {b.skills.map((s) => <span key={s} className={`chip ${wanted.has(s) ? 'chip-match' : 'chip-plain'}`}>{s}</span>)}
              </span>
              {at !== undefined && (
                <span className="actions">
                  <button type="button" className="small-button" disabled={at === 0} onClick={() => onMove(b.id, -1)}
                    aria-label={`Move up: ${firstWords(b.text)}`}>↑</button>
                  <button type="button" className="small-button" disabled={at === chosen.length - 1} onClick={() => onMove(b.id, 1)}
                    aria-label={`Move down: ${firstWords(b.text)}`}>↓</button>
                </span>
              )}
            </li>
          );
        })}
      </ul>
    </fieldset>
  );
}
