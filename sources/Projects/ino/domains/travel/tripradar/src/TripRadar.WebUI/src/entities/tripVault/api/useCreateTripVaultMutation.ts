import { useMutation, useQueryClient } from '@tanstack/react-query';
import { tripVaultApi } from './tripVaultApi';
import type { CreateTripVaultRequest } from './types';

export const useCreateTripVaultMutation = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (request: CreateTripVaultRequest) => tripVaultApi.createTripVault(request),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['trip-vaults'] });
    },
  });
};
