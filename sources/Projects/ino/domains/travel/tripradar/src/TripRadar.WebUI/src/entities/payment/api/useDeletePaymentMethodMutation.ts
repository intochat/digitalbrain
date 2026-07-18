import { useMutation, useQueryClient } from '@tanstack/react-query';
import type { DeletePaymentMethodByCardRequest } from 'shared/api';
import { paymentApi } from './paymentApi';
import { paymentKeys } from './queryKeys';

export const useDeletePaymentMethodMutation = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (data: DeletePaymentMethodByCardRequest) => paymentApi.deletePaymentMethod(data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: paymentKeys.paymentMethods() });
    },
  });
};
