import { apiClient } from 'shared/api/interceptors';

const SESSION_KEY = 'tripradar.telegramBind.chatId';

export const TELEGRAM_SOURCE_PARAM = 'source';
export const TELEGRAM_CHAT_ID_PARAM = 'chatId';
export const TELEGRAM_PROVIDER_PARAM = 'provider';
export const TELEGRAM_SOURCE_VALUE = 'telegram';

export const readTelegramChatIdFromUrl = (searchParams: URLSearchParams): string | null => {
  if (searchParams.get(TELEGRAM_SOURCE_PARAM) !== TELEGRAM_SOURCE_VALUE) {
    return null;
  }
  const chatId = searchParams.get(TELEGRAM_CHAT_ID_PARAM);
  if (!chatId || !/^-?\d+$/.test(chatId)) {
    return null;
  }
  return chatId;
};

export const rememberTelegramChatId = (chatId: string): void => {
  try {
    window.sessionStorage.setItem(SESSION_KEY, chatId);
  } catch {
    // sessionStorage unavailable — non-fatal
  }
};

export const consumeTelegramChatId = (): string | null => {
  try {
    const value = window.sessionStorage.getItem(SESSION_KEY);
    if (value) window.sessionStorage.removeItem(SESSION_KEY);
    return value;
  } catch {
    return null;
  }
};

export const notifyTelegramAfterLogin = async (chatId: string): Promise<boolean> => {
  const numericChatId = Number.parseInt(chatId, 10);
  if (!Number.isFinite(numericChatId) || numericChatId <= 0) {
    return false;
  }
  try {
    await apiClient.patch('/api/v1/users/profile/telegram', { telegramUserId: numericChatId });
    return true;
  } catch (error) {
    console.error('Failed to notify Telegram chat after login:', error);
    return false;
  }
};
