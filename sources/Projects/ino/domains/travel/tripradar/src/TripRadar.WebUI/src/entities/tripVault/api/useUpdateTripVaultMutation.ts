import { useMutation, useQueryClient } from '@tanstack/react-query';
import { tripVaultApi } from './tripVaultApi';
import type { UpdateTripVaultRequest } from './types';

interface UpdateTripVaultParams {
  tripUniqueId: string;
  request: UpdateTripVaultRequest;
}

export const useUpdateTripVaultMutation = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ tripUniqueId, request }: UpdateTripVaultParams) =>
      tripVaultApi.updateTripVault(tripUniqueId, request),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['trip-vaults'] });
    },
  });
};
