import { fireEvent, screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { SalaryView } from '../api';
import { ABU_DHABI_META, META, mockApi, renderRoute } from '../test/render';
import { Salaries } from './Salaries';

const VIEW: SalaryView = {
  postings: 12,
  withSalary: 7,
  cells: [
    { family: 'Dotnet', seniority: 'Senior', count: 6, median: 150000, p25: 130000, p75: 170000 },
    { family: 'LeadManager', seniority: 'Manager', count: 1, median: null, p25: null, p75: null },
  ],
  histogram: [{ from: 120000, to: 140000, count: 3 }, { from: 140000, to: 160000, count: 4 }],
  examples: [],
};
const NO_SKILLS = { postings: 0, skills: [] };

describe('Salaries page', () => {
  it('shows percentiles where there are enough salaries and marks thin cells', async () => {
    mockApi({ '/api/meta': META, '/api/skills': NO_SKILLS, '/api/salaries': VIEW });
    renderRoute('/salaries', <Salaries />);

    expect(await screen.findByText('$150k')).toBeInTheDocument();
    expect(screen.getByText('n < 5')).toBeInTheDocument();
    expect(screen.getByText(/7 of 12 postings/)).toBeInTheDocument();
  });

  it('sends the chosen company to the API', async () => {
    const calls = mockApi({ '/api/meta': META, '/api/skills': NO_SKILLS, '/api/salaries': VIEW });
    renderRoute('/salaries', <Salaries />);
    await screen.findByRole('option', { name: 'Acme' });

    fireEvent.change(screen.getByLabelText('Company'), { target: { value: 'Acme' } });

    await waitFor(() => expect(calls).toContain('/api/salaries?company=Acme'));
  });

  it('says so when no posting in the selection has a salary', async () => {
    mockApi({ '/api/meta': META, '/api/skills': NO_SKILLS, '/api/salaries': { ...VIEW, withSalary: 0, cells: [], histogram: [] } });
    renderRoute('/salaries', <Salaries />);
    expect(await screen.findByText(/No salaried postings match these filters/)).toBeInTheDocument();
  });

  it('names the regions currency', async () => {
    mockApi({ '/api/meta': ABU_DHABI_META, '/api/skills': NO_SKILLS, '/api/salaries': VIEW });
    renderRoute('/salaries', <Salaries />);
    expect(await screen.findByText(/yearly midpoints in AED/)).toBeInTheDocument();
  });

  it('says nothing region-specific when no posting has a salary', async () => {
    mockApi({ '/api/meta': ABU_DHABI_META, '/api/skills': NO_SKILLS, '/api/salaries': { ...VIEW, withSalary: 0, cells: [], histogram: [] } });
    renderRoute('/salaries', <Salaries />);
    expect(await screen.findByText(/No salaried postings match these filters/)).not.toHaveTextContent('Ontario');
  });
});
