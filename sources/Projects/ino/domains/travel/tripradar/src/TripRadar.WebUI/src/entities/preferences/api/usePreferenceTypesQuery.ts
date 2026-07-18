import { useQuery } from '@tanstack/react-query';
import type { ServiceType } from 'shared/api';
import { withRetry } from 'shared/lib/retry/retryUtils';
import { preferencesApi } from './preferencesApi';

export const usePreferenceTypesQuery = () => {
  return useQuery({
    queryKey: ['preferences', 'types'],
    queryFn: () =>
      withRetry(() => preferencesApi.getPreferenceTypes(), {
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
    staleTime: 10 * 60 * 1000, // 10 minutes
    retry: false, // We handle retries in the queryFn
  });
};

export const usePreferenceTypesByServiceQuery = (serviceType?: ServiceType) => {
  return useQuery({
    queryKey: ['preferences', 'types', serviceType],
    queryFn: () =>
      withRetry(() => preferencesApi.getPreferenceTypesByService(serviceType as ServiceType), {
        maxAttempts: 3,
        baseDelayMs: 1000,
        shouldRetry: error => {
          if (error && typeof error === 'object' && 'response' in error) {
            const apiError = error as { response?: { status?: number } };
            const status = apiError.response?.status;
            return status !== 400 && status !== 401 && status !== 404;
          }
          return true;
        },
      }),
    enabled: !!serviceType,
    staleTime: 5 * 60 * 1000, // 5 minutes
    retry: false, // We handle retries in the queryFn
  });
};
