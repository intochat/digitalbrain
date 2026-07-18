import { useMutation, useQueryClient } from '@tanstack/react-query';
import type { DowngradeTierRequest } from 'shared/api';
import { paymentApi } from './paymentApi';
import { paymentKeys } from './queryKeys';

export const useDowngradeSubscriptionMutation = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (data: DowngradeTierRequest) => paymentApi.downgradeSubscription(data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: paymentKeys.subscription() });
    },
  });
};
