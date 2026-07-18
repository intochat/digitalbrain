import { useMutation } from '@tanstack/react-query';
import { paymentApi } from './paymentApi';

export const useCreateSetupIntentMutation = () => {
  return useMutation({
    mutationFn: () => paymentApi.createSetupIntent(),
  });
};
