import { useMutation } from '@tanstack/react-query';
import type { CreateSubscriptionCheckoutRequest } from 'shared/api';
import { paymentApi } from './paymentApi';

export const useCreateCheckoutMutation = () => {
  return useMutation({
    mutationFn: (data: CreateSubscriptionCheckoutRequest) => paymentApi.createCheckout(data),
  });
};
