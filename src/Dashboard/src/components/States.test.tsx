import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { ApiError } from '../api';
import { ErrorBanner, QueryState, StaleBanner } from './States';

describe('shared states', () => {
  it("shows the API's message in an alert", () => {
    render(<ErrorBanner error={new ApiError("'Cobol' is not in config/skills.json.", 404)} />);
    expect(screen.getByRole('alert')).toHaveTextContent("'Cobol' is not in config/skills.json.");
  });

  it('shows loading while pending and the error once it fails', () => {
    const { rerender } = render(<QueryState query={{ isPending: true, error: null }} what="skills" />);
    expect(screen.getByRole('status')).toHaveTextContent('Loading skills');
    rerender(<QueryState query={{ isPending: false, error: new ApiError('Down', 503) }} what="skills" />);
    expect(screen.getByRole('alert')).toHaveTextContent('Down');
  });

  // The refresh can fail without the database being down (an inbox file locked mid-save), so the banner must not
  // send the owner to Docker as the only cause.
  it('says the refresh failed and names both places to look', () => {
    render(<StaleBanner />);
    const banner = screen.getByRole('status');
    expect(banner).toHaveTextContent('from the last successful load');
    expect(banner).toHaveTextContent("If Docker Desktop isn't running");
    expect(banner).toHaveTextContent('Aspire dashboard');
  });
});
