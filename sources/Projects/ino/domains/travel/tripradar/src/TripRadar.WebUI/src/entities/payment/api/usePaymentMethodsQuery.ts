import { useQuery } from '@tanstack/react-query';
import { paymentApi } from './paymentApi';
import { paymentKeys } from './queryKeys';

export const usePaymentMethodsQuery = () => {
  return useQuery({
    queryKey: paymentKeys.paymentMethods(),
    queryFn: paymentApi.getPaymentMethods,
  });
};
