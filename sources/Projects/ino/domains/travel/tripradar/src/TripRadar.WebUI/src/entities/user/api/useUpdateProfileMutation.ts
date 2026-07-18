import { useMutation, useQueryClient } from '@tanstack/react-query';
import type { UpdateUserProfileRequest } from 'shared/api';
import { profileApi } from './profileApi';

export const useUpdateProfileMutation = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (data: UpdateUserProfileRequest) => profileApi.updateProfile(data),
    onSuccess: async updatedProfile => {
      queryClient.setQueryData(['profile'], updatedProfile);
      await queryClient.invalidateQueries({
        queryKey: ['profile'],
        refetchType: 'active',
      });
    },
  });
};
