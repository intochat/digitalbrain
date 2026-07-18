import { useQuery } from '@tanstack/react-query';
import { withRetry } from 'shared/lib/retry/retryUtils';
import { preferencesApi } from './preferencesApi';

export const useUserPreferencesQuery = () => {
  return useQuery({
    queryKey: ['preferences'],
    queryFn: () =>
      withRetry(() => preferencesApi.getUserPreferences(), {
        maxAttempts: 3,
        baseDelayMs: 1000,
        shouldRetry: error => {
          // Don't retry on 404 (user has no preferences yet) or 401 (unauthorized)
          if (error && typeof error === 'object' && 'response' in error) {
            const apiError = error as { response?: { status?: number } };
            const status = apiError.response?.status;
            return status !== 404 && status !== 401;
          }
          return true;
        },
      }),
    staleTime: 5 * 60 * 1000, // 5 minutes
    retry: false, // We handle retries in the queryFn
  });
};
