import { useQuery } from '@tanstack/react-query';
import { portalApi } from './portalApi';

export const usePortalCurrenciesQuery = () => {
  return useQuery({
    queryKey: ['portal', 'currencies'],
    queryFn: portalApi.getCurrencies,
    staleTime: 5 * 60 * 1000, // 5 minutes
  });
};
