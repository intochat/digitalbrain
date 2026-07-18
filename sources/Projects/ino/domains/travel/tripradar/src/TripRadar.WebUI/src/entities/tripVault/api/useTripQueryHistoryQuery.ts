import { useQuery } from '@tanstack/react-query';
import { tripVaultApi } from './tripVaultApi';

interface UseTripQueryHistoryQueryParams {
  tripUniqueId: string | null;
  pageNumber: number;
  pageSize: number;
  enabled?: boolean;
}

export const useTripQueryHistoryQuery = ({
  tripUniqueId,
  pageNumber,
  pageSize,
  enabled = true,
}: UseTripQueryHistoryQueryParams) => {
  return useQuery({
    queryKey: ['trip-vault-history', tripUniqueId, pageNumber, pageSize],
    queryFn: () => tripVaultApi.getTripQueryHistory(tripUniqueId!, pageNumber, pageSize),
    enabled: enabled && !!tripUniqueId,
  });
};
