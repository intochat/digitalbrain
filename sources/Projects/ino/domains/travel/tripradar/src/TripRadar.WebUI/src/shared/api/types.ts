export interface ApiResponse<T> {
  data: T;
  message?: string;
  success: boolean;
}

export interface ApiError {
  message: string;
  code?: string;
}

export interface CreateTelegramLoginRequest {
  telegramAuth?: TelegramAuthApiData;
  initData?: string;
}

// Telegram Integration Types

/**
 * Data received from Telegram OAuth widget
 */
export interface TelegramData {
  id: number;
  first_name: string;
  last_name?: string;
  username?: string;
  photo_url?: string;
  auth_date: number;
  hash: string;
}

/**
 * Telegram auth payload expected by backend contracts (camelCase).
 */
export interface TelegramAuthApiData {
  id: number;
  firstName: string;
  lastName?: string | null;
  username: string;
  photoUrl?: string | null;
  authDate: number;
  hash: string;
  rawInitData?: string | null;
}

/**
 * Request to link Telegram account to user
 * Uses email to identify the user (from email confirmation or login error)
 */
export interface LinkTelegramRequest {
  email: string;
  telegramAuth: TelegramAuthApiData;
}

/**
 * Request to sync Telegram username for an existing account.
 */
export interface TelegramUsernameSyncRequest {
  telegramAuth: TelegramAuthApiData;
}

/**
 * User data returned from API
 */
export interface User {
  username: string;
  email: string;
  firstName?: string;
  lastName?: string;
  telegramId?: number;
  isEmailConfirmed?: boolean;
  profilePictureUrl?: string;
  timezoneId?: number;
  languageCode?: string;
  countryCode?: string;
  allowsMarketingEmails?: boolean;
  isActive?: boolean;
  tierName?: string;
}

/**
 * Response from linking Telegram account
 * Browser clients use HttpOnly cookies and may receive null token fields.
 * API clients can still receive token values directly.
 */
export interface LinkTelegramResponse {
  token?: string | null;
  refreshToken?: string | null;
  email: string;
  username: string;
  message?: string | null;
}

/**
 * Error response when Telegram linking is required
 */
export interface LoginErrorTelegramRequired {
  error: 'TELEGRAM_REQUIRED';
  message: string;
  email: string;
}
