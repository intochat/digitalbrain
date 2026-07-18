import { useMutation } from '@tanstack/react-query';
import { apiClient, type ForgotPasswordRequest } from 'shared/api';

export const useForgotPasswordMutation = () => {
  return useMutation<void, Error, ForgotPasswordRequest>({
    mutationFn: async data => {
      return apiClient.post('/api/v1/users/password-reset-requests', data);
    },
  });
};
