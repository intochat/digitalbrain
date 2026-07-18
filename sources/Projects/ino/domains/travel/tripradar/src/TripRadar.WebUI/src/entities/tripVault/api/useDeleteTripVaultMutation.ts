import { useMutation, useQueryClient } from '@tanstack/react-query';
import { tripVaultApi } from './tripVaultApi';

interface DeleteTripVaultParams {
  tripUniqueId: string;
}

export const useDeleteTripVaultMutation = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ tripUniqueId }: DeleteTripVaultParams) => tripVaultApi.deleteTripVault(tripUniqueId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['trip-vaults'] });
      queryClient.invalidateQueries({ queryKey: ['trip-vault-history'] });
    },
  });
};
