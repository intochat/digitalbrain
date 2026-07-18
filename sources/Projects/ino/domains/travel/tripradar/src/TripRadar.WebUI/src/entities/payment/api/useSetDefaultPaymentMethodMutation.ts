import { useMutation, useQueryClient } from '@tanstack/react-query';
import type { UpdateDefaultPaymentMethodRequest } from 'shared/api';
import { paymentApi } from './paymentApi';
import { paymentKeys } from './queryKeys';

export const useSetDefaultPaymentMethodMutation = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (data: UpdateDefaultPaymentMethodRequest) => paymentApi.setDefaultPaymentMethod(data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: paymentKeys.paymentMethods() });
    },
  });
};
