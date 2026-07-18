import { useQuery } from '@tanstack/react-query';
import type { UsageEventsQueryParams } from './types';
import { usageApi } from './usageApi';

interface UseUsageEventsQueryOptions extends UsageEventsQueryParams {
  enabled?: boolean;
}

export const useUsageEventsQuery = ({ enabled = true, ...query }: UseUsageEventsQueryOptions) => {
  return useQuery({
    queryKey: ['usage-events', query],
    queryFn: () => usageApi.getUsageEvents(query),
    enabled,
  });
};
