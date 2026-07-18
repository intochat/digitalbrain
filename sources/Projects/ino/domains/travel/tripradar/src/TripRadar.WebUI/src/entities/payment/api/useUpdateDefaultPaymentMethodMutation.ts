import { useMutation, useQueryClient } from '@tanstack/react-query';
import { paymentApi } from './paymentApi';
import type { UpdateDefaultPaymentMethodRequest } from './types';

export const useUpdateDefaultPaymentMethodMutation = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (data: UpdateDefaultPaymentMethodRequest) => paymentApi.updateDefaultPaymentMethod(data),
    onSuccess: async () => {
      await queryClient.invalidateQueries({
        queryKey: ['payment', 'methods'],
        refetchType: 'active',
      });
    },
  });
};
