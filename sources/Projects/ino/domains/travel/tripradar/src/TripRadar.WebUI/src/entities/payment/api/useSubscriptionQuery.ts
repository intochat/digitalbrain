import { useQuery } from '@tanstack/react-query';
import { paymentApi } from './paymentApi';
import { paymentKeys } from './queryKeys';

interface UseSubscriptionQueryOptions {
  enabled?: boolean;
}

export const useSubscriptionQuery = ({ enabled = true }: UseSubscriptionQueryOptions = {}) => {
  return useQuery({
    queryKey: paymentKeys.subscription(),
    queryFn: paymentApi.getSubscription,
    enabled,
  });
};
