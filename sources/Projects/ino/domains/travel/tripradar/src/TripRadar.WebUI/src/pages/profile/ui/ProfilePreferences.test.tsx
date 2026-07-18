import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { ProfilePreferences } from './ProfilePreferences';

vi.mock('features/preferences', () => ({
  UserPreferencesSection: () => <div data-testid="user-preferences-section">Preferences section</div>,
}));

vi.mock('./ProfileLayout', () => ({
  ProfileLayout: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}));

describe('ProfilePreferences', () => {
  it('renders preferences section without duplicated header block', () => {
    render(<ProfilePreferences />);

    expect(screen.getByTestId('user-preferences-section')).toBeInTheDocument();
    expect(screen.queryByText('Customize your experience and settings')).not.toBeInTheDocument();
  });
});
