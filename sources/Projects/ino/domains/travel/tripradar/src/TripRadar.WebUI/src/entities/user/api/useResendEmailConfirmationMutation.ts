import { useMutation } from '@tanstack/react-query';
import { securityApi, type ResendEmailConfirmationRequest } from './securityApi';

export const useResendEmailConfirmationMutation = () => {
  return useMutation({
    mutationFn: (request: ResendEmailConfirmationRequest) => securityApi.resendEmailConfirmation(request),
  });
};
