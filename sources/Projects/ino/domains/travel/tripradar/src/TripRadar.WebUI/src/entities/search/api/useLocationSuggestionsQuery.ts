import { useQuery } from '@tanstack/react-query';
import { searchApi } from './searchApi';

interface UseLocationSuggestionsQueryOptions {
  enabled?: boolean;
}

export const useLocationSuggestionsQuery = (
  query: string,
  limit = 8,
  { enabled = true }: UseLocationSuggestionsQueryOptions = {}
) => {
  const normalizedQuery = query.trim();

  return useQuery({
    queryKey: ['location-suggestions', normalizedQuery, limit],
    queryFn: () => searchApi.searchLocations(normalizedQuery, limit),
    enabled: enabled && normalizedQuery.length >= 2,
    staleTime: 30_000,
  });
};
