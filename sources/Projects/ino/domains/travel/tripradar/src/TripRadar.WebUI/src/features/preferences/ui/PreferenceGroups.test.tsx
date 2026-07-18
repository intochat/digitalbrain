import { render, screen } from '@testing-library/react';
import { describe, it, expect, vi } from 'vitest';
import {
  FlightPreferenceGroup,
  HotelPreferenceGroup,
  LocalPlacesPreferenceGroup,
  MapsPreferenceGroup,
  EventPreferenceGroup,
} from './index';

describe('Preference Group Components', () => {
  const mockOnChange = vi.fn();

  it('should render FlightPreferenceGroup without errors', () => {
    const preferences = {
      Adults: 2,
      Children: 0,
      TravelClass: 'economy' as const,
      Currency: 'USD',
    };

    render(<FlightPreferenceGroup preferences={preferences} onChange={mockOnChange} />);
    expect(screen.getByText('Flight Preferences')).toBeInTheDocument();
    expect(screen.getByText('Adults')).toBeInTheDocument();
    expect(screen.getByText('Travel Class')).toBeInTheDocument();
  });

  it('should render HotelPreferenceGroup without errors', () => {
    const preferences = {
      Adults: 2,
      Children: 0,
      Currency: 'USD',
    };

    render(<HotelPreferenceGroup preferences={preferences} onChange={mockOnChange} />);
    expect(screen.getByText('Hotel Preferences')).toBeInTheDocument();
    expect(screen.getByText('Adults')).toBeInTheDocument();
    expect(screen.getByText('Free Cancellation')).toBeInTheDocument();
  });

  it('should render all service preference groups without errors', () => {
    const testCases = [
      {
        Component: LocalPlacesPreferenceGroup,
        preferences: { Currency: 'USD', Language: 'en' },
        title: 'Local Places Preferences',
      },
      {
        Component: MapsPreferenceGroup,
        preferences: { Currency: 'USD', Language: 'en' },
        title: 'Maps Preferences',
      },
      {
        Component: EventPreferenceGroup,
        preferences: { Currency: 'USD', Language: 'en' },
        title: 'Event Preferences',
      },
    ];

    testCases.forEach(({ Component, preferences, title }) => {
      const { unmount } = render(<Component preferences={preferences} onChange={mockOnChange} />);
      expect(screen.getByText(title)).toBeInTheDocument();
      unmount();
    });
  });
});
