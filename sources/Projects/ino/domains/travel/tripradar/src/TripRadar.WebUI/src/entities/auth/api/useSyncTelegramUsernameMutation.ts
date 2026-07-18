import { useMutation } from '@tanstack/react-query';
import { apiClient } from 'shared/api';
import type { LinkTelegramResponse, TelegramUsernameSyncRequest } from 'shared/api/types';

export const useSyncTelegramUsernameMutation = () => {
  return useMutation({
    mutationFn: (data: TelegramUsernameSyncRequest): Promise<LinkTelegramResponse> =>
      apiClient.patch('/api/v1/users/telegram-username-sync', data),
  });
};
