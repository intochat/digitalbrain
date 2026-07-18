import { render, screen } from '@testing-library/react';
import type { TripHistoryItem } from 'entities/tripVault';
import { HistoryItemCard } from './HistoryItemCard';

describe('HistoryItemCard', () => {
  it('falls back to generic rendering when a typed card payload cannot be parsed', () => {
    const item: TripHistoryItem = {
      uniqueId: '4f1fbc3f-d6f6-4ea8-97fc-c19602939d8f',
      serviceType: 'Event',
      queryParametersJson: '{"searchQuery":{"q":"new york events"}}',
      resultSummary: 'not-valid-json',
      createdOn: '2026-02-16T09:00:00Z',
      startDateTime: null,
      endDateTime: null,
    };

    render(<HistoryItemCard item={item} />);

    expect(screen.getByRole('button', { name: /show raw data/i })).toBeInTheDocument();
  });
});
