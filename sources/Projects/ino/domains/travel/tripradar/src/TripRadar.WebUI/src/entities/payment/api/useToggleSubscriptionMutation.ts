import { useMutation, useQueryClient } from '@tanstack/react-query';
import type { ToggleSubscriptionRequest } from 'shared/api';
import { paymentApi } from './paymentApi';
import { paymentKeys } from './queryKeys';

export const useToggleSubscriptionMutation = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (data: ToggleSubscriptionRequest) => paymentApi.toggleSubscription(data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: paymentKeys.subscription() });
    },
  });
};
