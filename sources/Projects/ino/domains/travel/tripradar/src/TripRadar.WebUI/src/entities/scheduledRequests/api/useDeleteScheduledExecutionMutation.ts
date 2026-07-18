import { useMutation, useQueryClient } from '@tanstack/react-query';
import { scheduledRequestsApi } from './scheduledRequestsApi';

interface DeleteScheduledExecutionParams {
  uniqueId: string;
}

export const useDeleteScheduledExecutionMutation = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ uniqueId }: DeleteScheduledExecutionParams) =>
      scheduledRequestsApi.deleteScheduledExecution(uniqueId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['scheduled-executions'] });
    },
  });
};
