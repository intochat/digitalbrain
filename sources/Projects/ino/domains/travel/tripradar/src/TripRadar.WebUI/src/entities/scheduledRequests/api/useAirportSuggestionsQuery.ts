import { useQuery } from '@tanstack/react-query';
import { scheduledRequestsApi } from './scheduledRequestsApi';

export const useAirportSuggestionsQuery = (query: string, limit = 8, enabled = true) => {
  const normalizedQuery = query.trim();

  return useQuery({
    queryKey: ['airport-suggestions', normalizedQuery, limit],
    queryFn: () => scheduledRequestsApi.searchAirports(normalizedQuery, limit),
    enabled: enabled && normalizedQuery.length >= 2,
    staleTime: 30_000,
  });
};
