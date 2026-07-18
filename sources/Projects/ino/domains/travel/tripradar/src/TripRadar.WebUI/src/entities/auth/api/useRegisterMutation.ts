import { useMutation } from '@tanstack/react-query';
import { apiClient, type CreateUserRequest, type UserManagementResponse } from 'shared/api';

export const useRegisterMutation = () => {
  return useMutation({
    mutationFn: (data: CreateUserRequest): Promise<UserManagementResponse> => apiClient.post('/api/v1/users', data),
  });
};
