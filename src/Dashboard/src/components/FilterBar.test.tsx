import { fireEvent, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { META, renderRoute } from '../test/render';
import { FilterBar } from './FilterBar';

const url = () => decodeURIComponent(screen.getByTestId('location').textContent ?? '');

describe('FilterBar', () => {
  it('ticks the profile families when the URL names none', () => {
    renderRoute('/skills', <FilterBar meta={META} />);
    expect(screen.getByLabelText('.NET')).toBeChecked();
    expect(screen.getByLabelText('Other')).not.toBeChecked();
  });

  it('writes changes to the URL, keeps other keys and returns to page 1', () => {
    renderRoute('/postings?page=3&q=bank', <FilterBar meta={META} />);

    fireEvent.click(screen.getByLabelText('Other'));
    fireEvent.change(screen.getByLabelText('Where'), { target: { value: 'remote' } });
    fireEvent.change(screen.getByLabelText('Window'), { target: { value: 'all' } });

    expect(url()).toContain('families=Dotnet,Fullstack,SoftwareEngineer,LeadManager,Other');
    expect(url()).toContain('where=remote');
    expect(url()).toContain('window=all');
    expect(url()).toContain('q=bank');
    expect(url()).not.toContain('page=');
  });

  it('keeps at least one role family ticked', () => {
    renderRoute('/?families=Dotnet', <FilterBar meta={META} />);
    expect(screen.getByLabelText('.NET')).toBeDisabled();
    expect(screen.getByLabelText('Other')).toBeEnabled();
  });

  it('filters by seniority and shows the choice in the summary', () => {
    renderRoute('/', <FilterBar meta={META} />);
    fireEvent.click(screen.getByLabelText('Senior'));
    expect(url()).toContain('seniority=Senior');
    expect(screen.getByText('Seniority: Senior')).toBeInTheDocument();
  });
});
