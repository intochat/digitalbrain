import { useMutation, useQueryClient } from '@tanstack/react-query';
import type { UserPreferences } from 'shared/api';
import { withRetry } from 'shared/lib/retry/retryUtils';
import { preferencesApi } from './preferencesApi';

interface UpdatePreferencesParams {
  preferences: UserPreferences;
}

export const useUpdatePreferencesMutation = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ preferences }: UpdatePreferencesParams) =>
      withRetry(() => preferencesApi.updateUserPreferences(preferences), {
        maxAttempts: 3,
        baseDelayMs: 1000,
        shouldRetry: error => {
          // Don't retry on client errors (4xx) except for 408 (timeout) and 429 (rate limit)
          if (error && typeof error === 'object' && 'response' in error) {
            const apiError = error as { response?: { status?: number } };
            const status = apiError.response?.status;
            if (status && status >= 400 && status < 500) {
              return status === 408 || status === 429;
            }
          }
          return true;
        },
      }),
    onMutate: async ({ preferences }) => {
      // Cancel any outgoing refetches
      await queryClient.cancelQueries({ queryKey: ['preferences'] });

      // Snapshot the previous value
      const previousPreferences = queryClient.getQueryData(['preferences']);

      // Optimistically update to the new value
      queryClient.setQueryData(['preferences'], {
        preferences: Object.entries(preferences).map(([key, value]) => ({
          preferenceTypeDisplayName: key,
          value: JSON.stringify(value),
        })),
      });

      // Return a context object with the snapshotted value
      return { previousPreferences };
    },
    onError: (_, __, context) => {
      // If the mutation fails, use the context returned from onMutate to roll back
      if (context?.previousPreferences) {
        queryClient.setQueryData(['preferences'], context.previousPreferences);
      }
    },
    onSuccess: () => {
      // Invalidate and refetch preferences after successful update
      queryClient.invalidateQueries({ queryKey: ['preferences'] });
    },
  });
};
