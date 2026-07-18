import { render, screen } from '@testing-library/react';
import type { TripHistoryItem } from 'entities/tripVault';
import { HistoryItemCard } from './HistoryItemCard';

const buildTruncatedSummary = (payload: Record<string, unknown>): string => {
  const serialized = JSON.stringify(payload);
  return JSON.stringify({
    truncated: true,
    originalLength: serialized.length,
    preview: JSON.stringify(serialized),
  });
};

const buildItem = (overrides: Partial<TripHistoryItem>): TripHistoryItem => ({
  uniqueId: 'a66f014c-f4fc-4f80-bceb-27e0cce5c9dc',
  serviceType: 'Event',
  queryParametersJson: '{}',
  resultSummary: null,
  createdOn: '2026-02-16T09:00:00Z',
  startDateTime: null,
  endDateTime: null,
  ...overrides,
});

describe('HistoryItemCard provider rendering', () => {
  it('renders Yelp place cards when serviceType is camelCase and payload is under place_results', () => {
    const item = buildItem({
      serviceType: 'yelpPlace',
      resultSummary: buildTruncatedSummary({
        place_results: {
          name: 'Four Barrel Coffee',
          rating: 4,
          reviews: 2245,
          categories: [{ title: 'Coffee & Tea' }],
          address: '375 Valencia St San Francisco, CA 94103',
        },
      }),
    });

    render(<HistoryItemCard item={item} />);

    expect(screen.getByText('Four Barrel Coffee')).toBeInTheDocument();
    expect(screen.queryByText('No search results found.')).not.toBeInTheDocument();
  });

  it('renders a friendly fallback message when a requested Yelp menu name is invalid', () => {
    const item = buildItem({
      serviceType: 'yelpPlaceFullMenu',
      resultSummary: buildTruncatedSummary({
        search_parameters: {
          menu_name: 'Lunch',
        },
        search_information: {
          full_menu_results_state:
            'Menu_name "Lunch" is not valid. Please check the list of available menu names for the current place E8RJkjfdcwgtyoPMjQ_Olg. Showing place results instead of full menu results.',
        },
        place_results: {
          name: 'Four Barrel Coffee',
          rating: 4,
          reviews: 2245,
        },
      }),
    });

    render(<HistoryItemCard item={item} />);

    expect(
      screen.getByText('Menu "Lunch" is not available for Four Barrel Coffee. Showing place results instead.')
    ).toBeInTheDocument();
    expect(screen.queryByText(/Menu_name "Lunch" is not valid/)).not.toBeInTheDocument();
  });

  it('renders TripAdvisor place card for place_result payload', () => {
    const item = buildItem({
      serviceType: 'tripAdvisorPlace',
      resultSummary: buildTruncatedSummary({
        place_result: {
          type: 'hotel',
          name: 'Candlewood Suites Roswell By IHG',
          rating: 4,
          reviews: 148,
          ranking: '#16 of 26 hotels in Roswell',
          address: '4 Military Heights Dr, Roswell, NM',
          website: 'https://www.tripadvisor.com',
        },
      }),
    });

    render(<HistoryItemCard item={item} />);

    expect(screen.getByText('Candlewood Suites Roswell By IHG')).toBeInTheDocument();
    expect(screen.queryByText('No TripAdvisor results found.')).not.toBeInTheDocument();
  });

  it('renders Google Light Search results from truncated preview payload', () => {
    const item = buildItem({
      serviceType: 'googleLightSearch',
      resultSummary: buildTruncatedSummary({
        organic_results: [
          {
            position: 1,
            title: '10-Day Weather Forecast for Paris, France - The Weather Channel',
            link: 'https://weather.com/weather/tenday/l/example',
            displayed_link: 'weather.com',
            snippet: 'Today. 50° / 36°. Rain.',
          },
        ],
      }),
    });

    render(<HistoryItemCard item={item} />);

    expect(screen.getByText('10-Day Weather Forecast for Paris, France - The Weather Channel')).toBeInTheDocument();
    expect(screen.queryByText('No Google search results found.')).not.toBeInTheDocument();
  });

  it('renders Maps place result card when payload has place_results object', () => {
    const item = buildItem({
      serviceType: 'mapsPlaceResults',
      resultSummary: buildTruncatedSummary({
        place_results: {
          title: 'Google Sydney - Pirrama Road',
          rating: 4,
          reviews: 1144,
          address: 'Ground Floor/48 Pirrama Rd, Pyrmont NSW 2009, Australia',
          phone: '+61 2 9374 4000',
          website: 'http://google.com/',
          type: ['Corporate office', 'Software company'],
        },
      }),
    });

    render(<HistoryItemCard item={item} />);

    expect(screen.getAllByText('Google Sydney - Pirrama Road').length).toBeGreaterThan(0);
  });
});
