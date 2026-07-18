import { useQuery } from '@tanstack/react-query';
import { withRetry } from 'shared/lib/retry/retryUtils';
import { preferencesApi } from './preferencesApi';

export const usePrivacyModeQuery = () => {
  return useQuery({
    queryKey: ['privacyMode'],
    queryFn: () =>
      withRetry(() => preferencesApi.getPrivacyMode(), {
        maxAttempts: 3,
        baseDelayMs: 1000,
        shouldRetry: error => {
          if (error && typeof error === 'object' && 'response' in error) {
            const apiError = error as { response?: { status?: number } };
            const status = apiError.response?.status;
            return status !== 401;
          }
          return true;
        },
      }),
    staleTime: 60 * 1000,
    retry: false,
  });
};
