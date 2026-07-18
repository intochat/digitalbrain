import { useMutation, useQueryClient } from '@tanstack/react-query';
import type { RefundRequest } from 'shared/api';
import { paymentApi } from './paymentApi';
import { paymentKeys } from './queryKeys';

export const useCreateRefundMutation = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (data: RefundRequest) => paymentApi.createRefund(data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: paymentKeys.invoices() });
    },
  });
};
