import { useMutation } from '@tanstack/react-query';
import { securityApi, type ChangePasswordRequest } from './securityApi';

export const useChangePasswordMutation = () => {
  return useMutation({
    mutationFn: (request: ChangePasswordRequest) => securityApi.changePassword(request),
  });
};
