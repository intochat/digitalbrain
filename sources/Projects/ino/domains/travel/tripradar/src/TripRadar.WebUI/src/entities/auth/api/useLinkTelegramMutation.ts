import { useMutation } from '@tanstack/react-query';
import { apiClient } from 'shared/api';
import type { LinkTelegramRequest, LinkTelegramResponse } from 'shared/api/types';

export const useLinkTelegramMutation = () => {
  return useMutation({
    mutationFn: (data: LinkTelegramRequest): Promise<LinkTelegramResponse> =>
      apiClient.patch('/api/v1/users/activation', data),
  });
};
