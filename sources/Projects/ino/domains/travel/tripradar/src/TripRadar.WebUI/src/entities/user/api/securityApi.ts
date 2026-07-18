import { apiClient, type UserManagementResponse } from 'shared/api';

export interface ResendEmailConfirmationRequest {
  email: string;
}

export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
}

export const securityApi = {
  resendEmailConfirmation: async (request: ResendEmailConfirmationRequest): Promise<UserManagementResponse> => {
    return apiClient.post('/api/v1/users/email-confirmation-requests', request);
  },

  changePassword: async (request: ChangePasswordRequest): Promise<UserManagementResponse> => {
    return apiClient.put('/api/v1/users/password', request);
  },

  deleteAccount: async (): Promise<UserManagementResponse> => {
    return apiClient.delete('/api/v1/users');
  },
};
