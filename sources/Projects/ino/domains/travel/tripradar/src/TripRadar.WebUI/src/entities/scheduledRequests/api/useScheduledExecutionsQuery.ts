import { useQuery } from '@tanstack/react-query';
import { scheduledRequestsApi } from './scheduledRequestsApi';

export const useScheduledExecutionsQuery = () => {
  return useQuery({
    queryKey: ['scheduled-executions'],
    queryFn: () => scheduledRequestsApi.getScheduledExecutions(),
  });
};
