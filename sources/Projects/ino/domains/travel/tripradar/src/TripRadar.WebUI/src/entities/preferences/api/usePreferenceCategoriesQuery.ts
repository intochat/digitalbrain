import { useQuery } from '@tanstack/react-query';
import { withRetry } from 'shared/lib/retry/retryUtils';
import { preferencesApi } from './preferencesApi';

export const usePreferenceCategoriesQuery = () => {
  return useQuery({
    queryKey: ['preferences', 'categories'],
    queryFn: () =>
      withRetry(() => preferencesApi.getPreferenceCategories(), {
        maxAttempts: 2,
        baseDelayMs: 1000,
        shouldRetry: error => {
          if (error && typeof error === 'object' && 'response' in error) {
            const apiError = error as { response?: { status?: number } };
            const status = apiError.response?.status;
            return status !== 401 && status !== 404;
          }

          return true;
        },
      }),
    staleTime: 10 * 60 * 1000,
    retry: false,
  });
};
