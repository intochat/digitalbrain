import { useMutation, useQueryClient } from '@tanstack/react-query';
import { scheduledRequestsApi } from './scheduledRequestsApi';
import type { CreateScheduledRequestPayload } from './types';

export const useCreateScheduledRequestMutation = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (request: CreateScheduledRequestPayload) => scheduledRequestsApi.createScheduledRequest(request),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['scheduled-executions'] });
    },
  });
};
