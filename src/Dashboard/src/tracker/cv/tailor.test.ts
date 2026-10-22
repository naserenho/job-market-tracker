import { describe, expect, it } from 'vitest';
import { PROPOSAL } from './sample';
import { bulletsShowing, chosenIn, coverage, findable, initialChoices, length, move, toggle, words } from './tailor';

describe('CV tailoring logic', () => {
  it('places each posting skill by what the ticked bullets show', () => {
    expect(Object.fromEntries(coverage(PROPOSAL, ['a1', 'a2', 'g1']))).toEqual({
      '.NET': 'bullet', Azure: 'listed', Docker: 'bullet', Kubernetes: 'available', 'SQL Server': 'missing',
    });
    expect(coverage(PROPOSAL, ['a1']).get('Docker')).toBe('available');
  });

  it('finds the bullets that show a skill', () => {
    expect([...bulletsShowing(PROPOSAL, 'Kubernetes')]).toEqual(['a3']);
  });

  it('starts from the saved choices when there are any', () => {
    expect(initialChoices(PROPOSAL)).toEqual(PROPOSAL.suggestion);
    const saved = { generatedOn: '2027-03-01', headline: 'Lead', summary: 'Hi.', bulletIds: ['a3'], droppedIds: [] };
    expect(initialChoices({ ...PROPOSAL, saved })).toEqual({ headline: 'Lead', summary: 'Hi.', bulletIds: ['a3'] });
  });

  it('ticks onto the end, unticks, and moves only within a section', () => {
    const ticked = toggle(PROPOSAL.suggestion, 'a3');
    expect(ticked.bulletIds).toEqual(['a1', 'a2', 'g1', 'a3']);
    expect(chosenIn(PROPOSAL.sections[0], ticked).map((b) => b.id)).toEqual(['a1', 'a2', 'a3']);

    const moved = move(ticked, PROPOSAL.sections[0], 'a3', -1);
    expect(chosenIn(PROPOSAL.sections[0], moved).map((b) => b.id)).toEqual(['a1', 'a3', 'a2']);
    expect(move(moved, PROPOSAL.sections[0], 'a1', -1)).toBe(moved);
    expect(toggle(moved, 'a2').bulletIds).toEqual(['a1', 'a3', 'g1']);
  });

  it('estimates the length from the words on the page', () => {
    expect(words('  Built   CI/CD \n pipelines ')).toBe(3);
    // 100 fixed + 3 headline + 3 summary + (5 + 4 + 2) bullet words = 117; 117 / 450 = 0.26
    expect(length(PROPOSAL, PROPOSAL.suggestion)).toEqual({ words: 117, pages: 0.3 });
  });

  it('finds the skills an unticked bullet shows', () => {
    expect([...findable(PROPOSAL, ['a1', 'a2', 'g1'])]).toEqual(['Kubernetes']);
    expect(findable(PROPOSAL, ['a1', 'a2', 'a3', 'g1']).size).toBe(0);
  });
});
