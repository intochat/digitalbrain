import { useQuery } from '@tanstack/react-query';
import { profileApi } from './profileApi';

interface UseProfileQueryOptions {
  enabled?: boolean;
}

export const useProfileQuery = (options?: UseProfileQueryOptions) => {
  return useQuery({
    queryKey: ['profile'],
    queryFn: () => profileApi.getProfile(),
    enabled: options?.enabled ?? true,
  });
};
