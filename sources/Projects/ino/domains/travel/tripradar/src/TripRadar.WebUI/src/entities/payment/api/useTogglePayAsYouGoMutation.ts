import { useMutation, useQueryClient } from '@tanstack/react-query';
import type { UpdateMeteredBillingRequest } from 'shared/api';
import { paymentApi } from './paymentApi';
import { paymentKeys } from './queryKeys';

export const useTogglePayAsYouGoMutation = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (data: UpdateMeteredBillingRequest) => paymentApi.togglePayAsYouGo(data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: paymentKeys.overageUsage() });
    },
  });
};
