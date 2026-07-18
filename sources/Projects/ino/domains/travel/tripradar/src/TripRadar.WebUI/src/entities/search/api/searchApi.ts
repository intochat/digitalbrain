import { apiClient } from 'shared/api';
import type { GetLocationSuggestionsResponse, LocationSuggestionItem } from './types';

const SEARCH_BASE_PATH = '/api/v1/search';

export const searchApi = {
  searchLocations: async (query: string, limit = 8): Promise<LocationSuggestionItem[]> => {
    const encodedQuery = encodeURIComponent(query.trim());

    try {
      const response = await apiClient.get<GetLocationSuggestionsResponse>(
        `${SEARCH_BASE_PATH}/locations?query=${encodedQuery}&limit=${limit}`,
        { skipUnauthorizedRedirect: true }
      );

      return response.locations ?? [];
    } catch {
      return [];
    }
  },
};
