import type { TelegramAuthApiData, TelegramData } from 'shared/api/types';
import {
  getTelegramAuthBaseUrl as getEnvAuthBaseUrl,
  getTelegramBotUsername as getEnvBotUsername,
} from 'shared/config/env';

/**
 * Extend the Window interface to include Telegram widget types
 */
declare global {
  interface Window {
    Telegram?: {
      Login?: {
        auth: (options: { bot_id: string; request_access?: string; lang?: string }) => void;
      };
    };
    onTelegramAuth?: (user: TelegramData) => void;
  }
}

/**
 * Load the Telegram Login Widget script dynamically
 * @returns Promise that resolves when the script is loaded
 * @throws Error if script fails to load
 */
export const loadTelegramWidget = (): Promise<void> => {
  return new Promise((resolve, reject) => {
    // Check if script is already loaded
    if (document.querySelector('script[src*="telegram-widget"]')) {
      resolve();
      return;
    }

    const script = document.createElement('script');
    script.src = 'https://telegram.org/js/telegram-widget.js?22';
    script.async = true;

    script.onload = () => {
      resolve();
    };

    script.onerror = () => {
      reject(new Error('Failed to load Telegram widget script'));
    };

    document.body.appendChild(script);
  });
};

/**
 * Validate that the received data matches the TelegramData structure
 * Type guard function to ensure all required fields are present
 * @param data - Unknown data to validate
 * @returns True if data is valid TelegramData, false otherwise
 */
export const validateTelegramData = (data: unknown): data is TelegramData => {
  if (!data || typeof data !== 'object') {
    return false;
  }

  const telegramData = data as Record<string, unknown>;

  // Check required fields
  const hasRequiredFields =
    typeof telegramData.id === 'number' &&
    typeof telegramData.first_name === 'string' &&
    typeof telegramData.auth_date === 'number' &&
    typeof telegramData.hash === 'string';

  if (!hasRequiredFields) {
    return false;
  }

  // Check optional fields if present
  if (telegramData.last_name !== undefined && typeof telegramData.last_name !== 'string') {
    return false;
  }

  if (telegramData.username !== undefined && typeof telegramData.username !== 'string') {
    return false;
  }

  if (telegramData.photo_url !== undefined && typeof telegramData.photo_url !== 'string') {
    return false;
  }

  return true;
};

/**
 * Get the Telegram bot username from environment variables
 * Re-exports the function from shared/config/env for convenience
 * @returns The Telegram bot username
 * @throws Error if VITE_TELEGRAM_BOT_USERNAME is not defined
 */
export const getTelegramBotUsername = (): string => {
  return getEnvBotUsername();
};

/**
 * Build Telegram auth callback URL.
 * Uses VITE_TELEGRAM_AUTH_BASE_URL when configured, otherwise current window origin.
 */
export const buildTelegramAuthUrl = (callbackPath: string): string => {
  const configuredBaseUrl = getEnvAuthBaseUrl();
  const currentOrigin = typeof window !== 'undefined' ? window.location.origin : 'http://localhost:3000';
  const baseUrl = configuredBaseUrl || currentOrigin;

  const normalizedPath = callbackPath.startsWith('/') ? callbackPath : `/${callbackPath}`;

  try {
    return new URL(normalizedPath, baseUrl).toString();
  } catch {
    return `${currentOrigin}${normalizedPath}`;
  }
};

/**
 * Convert Telegram widget data (snake_case) to API format (camelCase)
 * @param telegramData - Data from Telegram widget in snake_case
 * @returns Data in camelCase format for API
 */
export const convertTelegramDataToApiFormat = (telegramData: TelegramData): TelegramAuthApiData => {
  return {
    id: telegramData.id,
    firstName: telegramData.first_name,
    lastName: telegramData.last_name || null,
    username: telegramData.username || '',
    photoUrl: telegramData.photo_url || null,
    authDate: telegramData.auth_date,
    hash: telegramData.hash,
  };
};
