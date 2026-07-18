import { useMutation, useQueryClient } from '@tanstack/react-query';
import { scheduledRequestsApi } from './scheduledRequestsApi';
import type { GetScheduledExecutionsResponse, UpdateScheduledExecutionConfigurationRequest } from './types';

interface UpdateScheduledExecutionConfigurationParams {
  uniqueId: string;
  configuration: UpdateScheduledExecutionConfigurationRequest;
}

const QUERY_KEY = ['scheduled-executions'];

export const useUpdateScheduledExecutionConfigurationMutation = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ uniqueId, configuration }: UpdateScheduledExecutionConfigurationParams) =>
      scheduledRequestsApi.updateScheduledExecutionConfiguration(uniqueId, configuration),
    onMutate: async ({ uniqueId, configuration }) => {
      await queryClient.cancelQueries({ queryKey: QUERY_KEY });
      const previous = queryClient.getQueryData<GetScheduledExecutionsResponse>(QUERY_KEY);
      queryClient.setQueryData<GetScheduledExecutionsResponse>(QUERY_KEY, old =>
        old
          ? {
              ...old,
              scheduledExecutions: old.scheduledExecutions.map(item =>
                item.scheduledExecutionUniqueId === uniqueId ? { ...item, isActive: configuration.isActive } : item
              ),
            }
          : old
      );
      return { previous };
    },
    onError: (_err, _vars, context) => {
      if (context?.previous) {
        queryClient.setQueryData(QUERY_KEY, context.previous);
      }
    },
    onSettled: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEY });
    },
  });
};
