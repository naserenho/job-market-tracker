import { screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { SkillDetail as Detail } from '../api';
import { failure, mockApi, renderRoute } from '../test/render';
import { SkillDetail } from './SkillDetail';

const DETAIL: Detail = {
  skill: 'CI/CD', category: 'devops', level: 'Working', share: 0.25, count: 10, postings: 40,
  salary: { withMedian: 150000, withCount: 6, withoutMedian: null, withoutCount: 3 },
  coSkills: [{ name: 'C#', count: 4 }], companies: [{ name: 'Acme', count: 3 }], examples: [],
};

describe('Skill detail page', () => {
  it.each([
    ['/skills/CI%2FCD', '/api/skills/CI%2FCD?text=full'],
    ['/skills/C%23', '/api/skills/C%23?text=full'],
    ['/skills/Estimation%20%26%20Planning', '/api/skills/Estimation%20%26%20Planning?text=full'],
  ])('reads %s from the route and asks the API for that skill', async (url, apiUrl) => {
    const calls = mockApi({ '/api/skills/': DETAIL });
    renderRoute(url, <SkillDetail />, '/skills/:name');
    await screen.findByRole('heading', { name: /CI\/CD/ });
    expect(calls).toContain(apiUrl);
  });

  it('compares salaries with and without the skill and links companies to their postings', async () => {
    mockApi({ '/api/skills/': DETAIL });
    renderRoute('/skills/CI%2FCD?where=gta', <SkillDetail />, '/skills/:name');

    expect(await screen.findByText('$150k')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Acme/ })).toHaveAttribute('href', '/postings?where=local&skill=CI%2FCD&company=Acme');
    expect(screen.getByRole('link', { name: /C#/ })).toHaveAttribute('href', '/skills/C%23?where=local');
  });

  it("shows the API's message for an unknown skill", async () => {
    mockApi({ '/api/skills/': failure(404, "'Cobol' is not in config/skills.json.") });
    renderRoute('/skills/Cobol', <SkillDetail />, '/skills/:name');
    expect(await screen.findByRole('alert')).toHaveTextContent("'Cobol' is not in config/skills.json.");
  });
});
