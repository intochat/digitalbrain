import { useQuery } from '@tanstack/react-query';
import { portalApi } from './portalApi';

export const usePortalTimezonesQuery = () => {
  return useQuery({
    queryKey: ['portal', 'timezones'],
    queryFn: portalApi.getTimezones,
    staleTime: 5 * 60 * 1000,
  });
};
