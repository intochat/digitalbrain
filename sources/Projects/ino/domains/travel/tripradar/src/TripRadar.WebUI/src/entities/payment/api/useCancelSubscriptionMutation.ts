import { useMutation, useQueryClient } from '@tanstack/react-query';
import type { CancelSubscriptionRequest } from 'shared/api';
import { paymentApi } from './paymentApi';
import { paymentKeys } from './queryKeys';

export const useCancelSubscriptionMutation = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (data: CancelSubscriptionRequest) => paymentApi.cancelSubscription(data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: paymentKeys.subscription() });
    },
  });
};
