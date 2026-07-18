import { useMutation, useQueryClient } from '@tanstack/react-query';
import { scheduledRequestsApi } from './scheduledRequestsApi';
import type { UpdateScheduledExecutionQueryRequest } from './types';

interface UpdateScheduledExecutionQueryParams {
  uniqueId: string;
  request: UpdateScheduledExecutionQueryRequest;
}

export const useUpdateScheduledExecutionQueryMutation = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ uniqueId, request }: UpdateScheduledExecutionQueryParams) =>
      scheduledRequestsApi.updateScheduledExecutionQuery(uniqueId, request),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['scheduled-executions'] });
    },
  });
};
