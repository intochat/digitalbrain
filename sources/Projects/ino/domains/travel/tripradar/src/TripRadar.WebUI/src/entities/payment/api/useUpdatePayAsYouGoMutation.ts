import { useMutation, useQueryClient } from '@tanstack/react-query';
import { paymentApi } from './paymentApi';
import type { UpdateMeteredBillingRequest } from './types';

export const useUpdatePayAsYouGoMutation = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (data: UpdateMeteredBillingRequest) => paymentApi.updatePayAsYouGo(data),
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({
          queryKey: ['payment', 'overage-usage'],
          refetchType: 'active',
        }),
        queryClient.invalidateQueries({
          queryKey: ['payment', 'subscription'],
          refetchType: 'active',
        }),
      ]);
    },
  });
};
