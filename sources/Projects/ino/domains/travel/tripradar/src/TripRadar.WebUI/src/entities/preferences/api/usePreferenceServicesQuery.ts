import { useQuery } from '@tanstack/react-query';
import { withRetry } from 'shared/lib/retry/retryUtils';
import { preferencesApi } from './preferencesApi';

export const usePreferenceServicesQuery = () => {
  return useQuery({
    queryKey: ['preferences', 'services'],
    queryFn: () =>
      withRetry(() => preferencesApi.getPreferenceServices(), {
        maxAttempts: 2,
        baseDelayMs: 1000,
        shouldRetry: error => {
          // Do not retry unsupported/unauthorized endpoints.
          if (error && typeof error === 'object' && 'response' in error) {
            const apiError = error as { response?: { status?: number } };
            const status = apiError.response?.status;
            return status !== 401 && status !== 404;
          }
          return true;
        },
      }),
    staleTime: 30 * 60 * 1000, // 30 minutes
    retry: false, // We handle retries in the queryFn
  });
};
