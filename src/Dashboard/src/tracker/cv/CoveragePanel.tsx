import type { CvProposal, ProfileLevel } from '../api';
import type { Coverage } from './tailor';

const GROUPS: { state: Coverage; title: string; hint: string }[] = [
  { state: 'bullet', title: 'In a bullet', hint: 'A ticked bullet shows it.' },
  { state: 'listed', title: 'Listed', hint: 'Only your skills list or a certification shows it. A button means an unticked bullet shows it too: click to find it.' },
  { state: 'available', title: 'Available', hint: 'Only unticked bullets show it. Click one to find them.' },
  { state: 'missing', title: 'Not in your CV', hint: 'Nothing in cv.json shows it. The page never adds a claim.' },
];

function missingText(level: ProfileLevel): string {
  return level === 'Strong' || level === 'Working' ? 'your profile says you know this: add a true bullet to cv.json' : 'gap';
}

// What the posting asks for, grouped by where the CV (as currently ticked) shows it.
export function CoveragePanel({ proposal, states, findable, highlight, onHighlight }: {
  proposal: CvProposal;
  states: Map<string, Coverage>;
  findable: Set<string>; // skills an unticked bullet shows
  highlight: string | null;
  onHighlight: (skill: string | null) => void;
}) {
  return (
    <section className="panel" aria-labelledby="h-coverage">
      <h3 id="h-coverage">What the posting asks for</h3>
      {proposal.postingSkills.length === 0 && <p className="empty">No skills found in the job text yet.</p>}
      {GROUPS.map(({ state, title, hint }) => {
        const skills = proposal.postingSkills.filter((s) => states.get(s.skill) === state);
        if (skills.length === 0) return null;
        return (
          <div key={state} className="coverage-group">
            <h4>{title} <span className="count">{skills.length}</span></h4>
            <p className="hint">{hint}</p>
            <ul className="skill-chips">
              {skills.map((s) => (
                <li key={s.skill}>
                  {(state === 'available' || state === 'listed') && findable.has(s.skill) ? (
                    <button type="button" className={`chip cover-${state}`} aria-pressed={highlight === s.skill}
                      onClick={() => onHighlight(highlight === s.skill ? null : s.skill)}>{s.skill}</button>
                  ) : (
                    <span className={`chip cover-${state}`}>{s.skill}</span>
                  )}
                  {state === 'missing' && <span className="hint"> {missingText(s.level)}</span>}
                </li>
              ))}
            </ul>
          </div>
        );
      })}
    </section>
  );
}
