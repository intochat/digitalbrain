import { useQuery } from '@tanstack/react-query';
import { paymentApi } from './paymentApi';
import { paymentKeys } from './queryKeys';

export const useOverageUsageQuery = () => {
  return useQuery({
    queryKey: paymentKeys.overageUsage(),
    queryFn: paymentApi.getOverageUsage,
  });
};
