import type {
  GetUserProfileResponse,
  RequestBehavior,
  UpdateUserProfileRequest,
  UpdateUserProfileResponse,
  UserManagementResponse,
} from 'shared/api';
import { apiClient } from 'shared/api';

export interface UnsubscribeMarketingParams {
  username?: string;
  email?: string;
}

export const profileApi = {
  getProfile: async (behavior?: RequestBehavior): Promise<GetUserProfileResponse> => {
    return apiClient.get('/api/v1/users/profile', behavior);
  },

  updateProfile: async (data: UpdateUserProfileRequest): Promise<UpdateUserProfileResponse> => {
    return apiClient.put('/api/v1/users/profile', data);
  },

  unsubscribeFromMarketingEmails: async ({
    username,
    email,
  }: UnsubscribeMarketingParams): Promise<UserManagementResponse> => {
    const query = new URLSearchParams();
    if (username) {
      query.set('username', username);
    }
    if (email) {
      query.set('email', email);
    }

    try {
      return await apiClient.patch(`/api/v1/users/marketing-emails/unsubscribe?${query.toString()}`);
    } catch (error) {
      const status = (error as { response?: { status?: number } })?.response?.status;
      if (status === 401 || status === 404) {
        await apiClient.put('/api/v1/users/profile', { allowsMarketingEmails: false });
        return { message: 'Unsubscribed from marketing emails successfully' };
      }

      throw error;
    }
  },
};
