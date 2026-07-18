import { useMutation } from '@tanstack/react-query';
import { apiClient, type ResetPasswordRequest } from 'shared/api';

export const useResetPasswordMutation = () => {
  return useMutation<void, Error, ResetPasswordRequest>({
    mutationFn: async data => {
      return apiClient.post('/api/v1/users/password-resets', data);
    },
  });
};
