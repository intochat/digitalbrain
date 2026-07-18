import { useQuery } from '@tanstack/react-query';
import { paymentApi } from './paymentApi';
import { paymentKeys } from './queryKeys';

export const useUsageSummaryQuery = () => {
  return useQuery({
    queryKey: paymentKeys.usageSummary(),
    queryFn: paymentApi.getUsageSummary,
  });
};
