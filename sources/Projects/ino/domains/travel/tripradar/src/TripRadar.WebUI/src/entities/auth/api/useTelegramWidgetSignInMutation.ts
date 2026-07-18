import { useMutation } from '@tanstack/react-query';
import { apiClient, type GetLoginResponse } from 'shared/api';
import type { CreateTelegramLoginRequest } from 'shared/api/types';

export const useTelegramWidgetSignInMutation = () => {
  return useMutation({
    mutationFn: (data: CreateTelegramLoginRequest): Promise<GetLoginResponse> =>
      apiClient.post('/api/v1/tokens/sessions/telegram', data),
  });
};
