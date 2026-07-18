import { useQuery } from '@tanstack/react-query';
import { portalApi } from './portalApi';

export const usePortalLanguagesQuery = () => {
  return useQuery({
    queryKey: ['portal', 'languages'],
    queryFn: portalApi.getLanguages,
    staleTime: 5 * 60 * 1000, // 5 minutes
  });
};
