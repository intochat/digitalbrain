import { useMutation, useQueryClient } from '@tanstack/react-query';
import { withRetry } from 'shared/lib/retry/retryUtils';
import { preferencesApi } from './preferencesApi';

export const useUpdatePrivacyModeMutation = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (enabled: boolean) =>
      withRetry(() => preferencesApi.updatePrivacyMode(enabled), {
        maxAttempts: 3,
        baseDelayMs: 1000,
        shouldRetry: error => {
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
    onMutate: async enabled => {
      await queryClient.cancelQueries({ queryKey: ['privacyMode'] });
      const previousPrivacyMode = queryClient.getQueryData(['privacyMode']);
      queryClient.setQueryData(['privacyMode'], { enabled });
      return { previousPrivacyMode };
    },
    onError: (_, __, context) => {
      if (context?.previousPrivacyMode) {
        queryClient.setQueryData(['privacyMode'], context.previousPrivacyMode);
      }
    },
    onSuccess: (_, enabled) => {
      queryClient.setQueryData(['privacyMode'], { enabled });
      queryClient.invalidateQueries({ queryKey: ['privacyMode'] });
      queryClient.invalidateQueries({ queryKey: ['preferences'] });
    },
  });
};
