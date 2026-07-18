import { useMutation } from '@tanstack/react-query';
import { apiClient, type CreateLoginRequest, type GetLoginResponse } from 'shared/api';

/**
 * Custom error type that includes email for TELEGRAM_REQUIRED errors
 */
export interface LoginError extends Error {
  email?: string;
  isTelegramRequired?: boolean;
  statusCode?: number;
}

export const useLoginMutation = () => {
  return useMutation({
    mutationFn: (data: CreateLoginRequest): Promise<GetLoginResponse> =>
      apiClient.post('/api/v1/tokens/sessions', data),
  });
};
