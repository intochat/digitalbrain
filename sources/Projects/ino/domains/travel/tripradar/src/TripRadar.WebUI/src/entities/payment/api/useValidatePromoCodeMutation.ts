import { useMutation } from '@tanstack/react-query';
import type { ValidatePromoCodeRequest } from 'shared/api';
import { paymentApi } from './paymentApi';

export const useValidatePromoCodeMutation = () => {
  return useMutation({
    mutationFn: (data: ValidatePromoCodeRequest) => paymentApi.validatePromoCode(data),
  });
};
