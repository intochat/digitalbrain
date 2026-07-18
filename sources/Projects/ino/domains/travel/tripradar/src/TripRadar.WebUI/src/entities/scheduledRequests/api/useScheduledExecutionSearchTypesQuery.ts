import { useQuery } from '@tanstack/react-query';
import { scheduledRequestsApi } from './scheduledRequestsApi';

export const useScheduledExecutionSearchTypesQuery = () => {
  return useQuery({
    queryKey: ['scheduled-execution-search-types'],
    queryFn: () => scheduledRequestsApi.getScheduledExecutionSearchTypes(),
  });
};
