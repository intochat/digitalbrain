import { useMutation, useQueryClient } from '@tanstack/react-query';
import { tripVaultApi } from './tripVaultApi';

interface RemoveTripItemParams {
  tripUniqueId: string;
  itemUniqueId: string;
}

export const useRemoveTripItemMutation = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ tripUniqueId, itemUniqueId }: RemoveTripItemParams) =>
      tripVaultApi.removeTripItemByUniqueId(tripUniqueId, itemUniqueId),
    onSuccess: (_, { tripUniqueId }) => {
      queryClient.invalidateQueries({ queryKey: ['trip-vault-history', tripUniqueId] });
      queryClient.invalidateQueries({ queryKey: ['trip-vaults'] });
    },
  });
};
