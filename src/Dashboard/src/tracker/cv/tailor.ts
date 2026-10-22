import type { CvChoices, CvProposal, ProposalBullet, ProposalSection } from '../api';

// Where the CV shows a posting skill: a ticked bullet, only the skills list or a certification, only unticked
// bullets, or nowhere in cv.json.
export type Coverage = 'bullet' | 'listed' | 'available' | 'missing';

export const WORDS_PER_PAGE = 450;
export const PAGE_LIMIT = 2;

export function coverage(proposal: CvProposal, chosen: string[]): Map<string, Coverage> {
  const picked = new Set(chosen);
  const bullets = proposal.sections.flatMap((s) => s.bullets);
  const inChosen = new Set(bullets.filter((b) => picked.has(b.id)).flatMap((b) => b.skills));
  const listed = new Set([
    ...proposal.skillGroups.flatMap((g) => g.items.flatMap((i) => i.skills)),
    ...proposal.certifications.flatMap((c) => c.skills),
  ]);
  const anywhere = new Set(bullets.flatMap((b) => b.skills));
  return new Map(proposal.postingSkills.map(({ skill }): [string, Coverage] => [
    skill,
    inChosen.has(skill) ? 'bullet' : listed.has(skill) ? 'listed' : anywhere.has(skill) ? 'available' : 'missing',
  ]));
}

// Skills an unticked bullet shows: their "Listed" and "Available" chips can point at those bullets.
export function findable(proposal: CvProposal, chosen: string[]): Set<string> {
  const picked = new Set(chosen);
  return new Set(proposal.sections.flatMap((s) => s.bullets).filter((b) => !picked.has(b.id)).flatMap((b) => b.skills));
}

// The bullets that show a skill: clicking a "listed" or "available" chip highlights them.
export function bulletsShowing(proposal: CvProposal, skill: string): Set<string> {
  return new Set(proposal.sections.flatMap((s) => s.bullets).filter((b) => b.skills.includes(skill)).map((b) => b.id));
}

// Where the page starts: the last generated CV's choices, or the fresh suggestion.
export function initialChoices(proposal: CvProposal): CvChoices {
  const saved = proposal.saved;
  return saved ? { headline: saved.headline, summary: saved.summary, bulletIds: saved.bulletIds } : proposal.suggestion;
}

// The ticked bullets of one section, in the order they will print.
export function chosenIn(section: ProposalSection, choices: CvChoices): ProposalBullet[] {
  const byId = new Map(section.bullets.map((b) => [b.id, b]));
  return choices.bulletIds.flatMap((id) => {
    const bullet = byId.get(id);
    return bullet ? [bullet] : [];
  });
}

// Ticking adds the bullet after its section's ticked ones (the list's order within a section is the print order);
// unticking removes it.
export function toggle(choices: CvChoices, bulletId: string): CvChoices {
  const on = choices.bulletIds.includes(bulletId);
  return { ...choices, bulletIds: on ? choices.bulletIds.filter((id) => id !== bulletId) : [...choices.bulletIds, bulletId] };
}

// Swaps a ticked bullet with the previous (-1) or next (1) ticked bullet of the same section.
export function move(choices: CvChoices, section: ProposalSection, bulletId: string, delta: -1 | 1): CvChoices {
  const mine = new Set(section.bullets.map((b) => b.id));
  const positions = choices.bulletIds.flatMap((id, i) => (mine.has(id) ? [i] : []));
  const at = positions.findIndex((i) => choices.bulletIds[i] === bulletId);
  const other = positions[at + delta];
  if (at < 0 || other === undefined) return choices;
  const ids = [...choices.bulletIds];
  [ids[positions[at]], ids[other]] = [ids[other], ids[positions[at]]];
  return { ...choices, bulletIds: ids };
}

export function words(text: string): number {
  return text.split(/\s+/).filter(Boolean).length;
}

// An estimate: only Word knows the real page count.
export function length(proposal: CvProposal, choices: CvChoices): { words: number; pages: number } {
  const picked = new Set(choices.bulletIds);
  const bulletWords = proposal.sections.flatMap((s) => s.bullets).filter((b) => picked.has(b.id)).reduce((n, b) => n + words(b.text), 0);
  const total = proposal.fixedWords + words(choices.headline) + words(choices.summary) + bulletWords;
  return { words: total, pages: Math.round((total / WORDS_PER_PAGE) * 10) / 10 };
}
