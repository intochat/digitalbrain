import { useQuery } from '@tanstack/react-query';
import { tripVaultApi } from './tripVaultApi';

interface UseTripVaultsQueryOptions {
  enabled?: boolean;
}

export const useTripVaultsQuery = ({ enabled = true }: UseTripVaultsQueryOptions = {}) => {
  return useQuery({
    queryKey: ['trip-vaults'],
    queryFn: () => tripVaultApi.getUserTrips(),
    enabled,
  });
};
