import type { LinkTelegramResponse } from 'shared/api/types';

interface TelegramAuthSuccessParams {
  response: LinkTelegramResponse;
  login: (user: { username: string; name: string; email: string; avatar: string; subscription: 'free' }) => void;
  navigate: (path: string, options?: { replace?: boolean }) => void;
  targetRoute: string;
}

export const handleTelegramAuthSuccess = ({
  response,
  login,
  navigate,
  targetRoute,
}: TelegramAuthSuccessParams): string | null => {
  if (!response.username) {
    return 'Username not received from server. Please try again.';
  }

  login({
    username: response.username,
    name: response.username,
    email: response.email,
    avatar: `https://ui-avatars.com/api/?name=${encodeURIComponent(response.username)}&background=6366f1&color=fff`,
    subscription: 'free',
  });

  navigate(targetRoute, { replace: true });
  return null;
};
