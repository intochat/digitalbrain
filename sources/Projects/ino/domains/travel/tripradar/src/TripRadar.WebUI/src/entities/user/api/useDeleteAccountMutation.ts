import { useMutation } from '@tanstack/react-query';
import { securityApi } from './securityApi';

export const useDeleteAccountMutation = () => {
  return useMutation({
    mutationFn: () => securityApi.deleteAccount(),
  });
};
