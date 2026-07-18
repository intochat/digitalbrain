import type { User } from 'app/types';

/**
 * Создает объект пользователя из данных регистрации
 */
export const createUserFromRegistration = (data: {
  username: string;
  email: string;
  firstName?: string;
  lastName?: string;
}): User => {
  const displayName = data.firstName && data.lastName ? `${data.firstName} ${data.lastName}` : data.username;

  return {
    username: data.username,
    name: displayName,
    email: data.email,
    avatar: `https://ui-avatars.com/api/?name=${encodeURIComponent(displayName)}&background=6366f1&color=fff`,
    subscription: 'free',
  };
};
