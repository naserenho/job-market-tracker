import { fireEvent, screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { SkillList } from '../api';
import { META, mockApi, renderRoute } from '../test/render';
import { Skills } from './Skills';

const LIST: SkillList = {
  postings: 40,
  skills: [
    { skill: 'Kubernetes', category: 'devops', share: 0.3, count: 12, level: 'None', previousShare: 0.2, change: 0.1 },
    { skill: 'C#', category: 'language', share: 0.5, count: 20, level: 'Strong', previousShare: null, change: null },
    { skill: 'CI/CD', category: 'devops', share: 0.1, count: 4, level: 'Working', previousShare: 0.15, change: -0.05 },
    { skill: 'Cobol', category: 'language', share: 0, count: 0, level: 'None', previousShare: null, change: null },
  ],
};

const skillColumn = () => screen.getAllByRole('row').slice(1).map((r) => within(r).getAllByRole('cell')[0].textContent);

describe('Skills page', () => {
  it('lists skills by share, leaves out unmentioned skills you lack, and links each to its page', async () => {
    mockApi({ '/api/meta': META, '/api/skills': LIST });
    renderRoute('/skills', <Skills />);

    expect(await screen.findByRole('link', { name: 'C#' })).toHaveAttribute('href', '/skills/C%23');
    expect(skillColumn()).toEqual(['C#', 'Kubernetes', 'CI/CD']);
    expect(screen.getByRole('link', { name: 'CI/CD' })).toHaveAttribute('href', '/skills/CI%2FCD');
  });

  it('sorts by the clicked column', async () => {
    mockApi({ '/api/meta': META, '/api/skills': LIST });
    renderRoute('/skills', <Skills />);
    await screen.findByRole('link', { name: 'C#' });

    fireEvent.click(screen.getByRole('button', { name: /Skill/ }));
    expect(skillColumn()).toEqual(['C#', 'CI/CD', 'Kubernetes']);

    fireEvent.click(screen.getByRole('button', { name: /Change/ }));
    expect(skillColumn()).toEqual(['Kubernetes', 'CI/CD', 'C#']);
  });

  it('asks the API for the chosen text kind and category', async () => {
    const calls = mockApi({ '/api/meta': META, '/api/skills': LIST });
    renderRoute('/skills?text=snippet&category=devops&where=gta', <Skills />);
    await screen.findByRole('link', { name: 'C#' });
    expect(calls).toContain('/api/skills?where=local&text=snippet&category=devops');
  });

  it('shows an empty state when no postings match', async () => {
    mockApi({ '/api/meta': META, '/api/skills': { postings: 0, skills: LIST.skills.map((s) => ({ ...s, share: 0, count: 0 })) } });
    renderRoute('/skills', <Skills />);
    expect(await screen.findByText(/No postings match these filters/)).toBeInTheDocument();
  });
});
