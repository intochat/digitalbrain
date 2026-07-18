const isDev = import.meta.env.DEV;

const readEnv = (key: string, devFallback = ''): string => {
  const value = import.meta.env[key];
  if (typeof value === 'string' && value.length > 0) {
    return value;
  }

  return isDev ? devFallback : '';
};

export const env = {
  API_BASE_URL: readEnv('VITE_API_BASE_URL', 'http://localhost:5330'),
  API_KEY: readEnv('VITE_API_KEY', '4599e588-6d05-4aad-86ed-3d28860a9338'),
  STRIPE_PUBLISHABLE_KEY: readEnv(
    'VITE_STRIPE_PUBLISHABLE_KEY',
    'pk_test_51RTefHB35G5bu7pWCTcxiWPaiCzYSJSVvW0sSW5vZWSwhfxNa9j27uFtD3dTxCl6U8SyAiQXVWhweq4Mf7UTqLnk00dxWIFkQf'
  ),
  APP_ENV: import.meta.env.VITE_APP_ENV || import.meta.env.MODE || 'development',
  TELEGRAM_BOT_USERNAME: readEnv('VITE_TELEGRAM_BOT_USERNAME', 'tripradar_auth_bot'),
  TELEGRAM_AUTH_BASE_URL: readEnv('VITE_TELEGRAM_AUTH_BASE_URL'),
  TELEGRAM_CLIENT_ID: readEnv('VITE_TELEGRAM_CLIENT_ID'),
  TELEGRAM_ENABLE_LOCAL_WIDGET: readEnv('VITE_TELEGRAM_ENABLE_LOCAL_WIDGET'),
  FIREBASE_API_KEY: readEnv('VITE_FIREBASE_API_KEY', 'AIzaSyCda9g7cIF77DTYdvTnJ5RThRAaALew99Y'),
  FIREBASE_AUTH_DOMAIN: readEnv('VITE_FIREBASE_AUTH_DOMAIN', 'trip-radar-466916.firebaseapp.com'),
  FIREBASE_PROJECT_ID: readEnv('VITE_FIREBASE_PROJECT_ID', 'trip-radar-466916'),
  FIREBASE_STORAGE_BUCKET: readEnv('VITE_FIREBASE_STORAGE_BUCKET', 'trip-radar-466916.firebasestorage.app'),
  FIREBASE_MESSAGING_SENDER_ID: readEnv('VITE_FIREBASE_MESSAGING_SENDER_ID', '759163782976'),
  FIREBASE_APP_ID: readEnv('VITE_FIREBASE_APP_ID', '1:759163782976:web:48fb6e25123205df6edca5'),
  FIREBASE_MEASUREMENT_ID: readEnv('VITE_FIREBASE_MEASUREMENT_ID', 'G-J0M8GT2E1S'),
  TELEMETRY_ENABLED: readEnv('VITE_TELEMETRY_ENABLED', 'true'),
  FRONTEND_ERROR_INGEST_URL: readEnv('VITE_FRONTEND_ERROR_INGEST_URL'),
  ANALYTICS_DEBUG: readEnv('VITE_ANALYTICS_DEBUG', 'false'),
  OTEL_ENABLED: import.meta.env.VITE_OTEL_ENABLED === 'true',
  OTEL_SERVICE_NAME: readEnv('VITE_OTEL_SERVICE_NAME', 'website'),
  OTEL_ENDPOINT: readEnv('VITE_OTEL_ENDPOINT'),
  OTEL_HEADERS: readEnv('VITE_OTEL_HEADERS'),
} as const;

export const isFirebaseAuthConfigured = (): boolean => {
  return (
    env.FIREBASE_API_KEY.length > 0 &&
    env.FIREBASE_AUTH_DOMAIN.length > 0 &&
    env.FIREBASE_PROJECT_ID.length > 0 &&
    env.FIREBASE_STORAGE_BUCKET.length > 0 &&
    env.FIREBASE_MESSAGING_SENDER_ID.length > 0 &&
    env.FIREBASE_APP_ID.length > 0
  );
};

/**
 * Get the Telegram bot username from environment variables
 * Uses a dev-only fallback for local runs.
 * @returns {string} The Telegram bot username
 */
export const getTelegramBotUsername = (): string => {
  return env.TELEGRAM_BOT_USERNAME;
};

export const getTelegramClientId = (): string => {
  return env.TELEGRAM_CLIENT_ID.trim();
};

/**
 * Get optional auth base URL override for Telegram widget callback.
 * When empty, current window origin is used.
 */
export const getTelegramAuthBaseUrl = (): string => {
  return env.TELEGRAM_AUTH_BASE_URL.trim();
};

/**
 * Enables Telegram Login Widget rendering on localhost-like hosts in development.
 * Defaults to enabled for local dev and can be overridden with VITE_TELEGRAM_ENABLE_LOCAL_WIDGET.
 */
export const isTelegramLocalWidgetEnabled = (): boolean => {
  const configuredValue = env.TELEGRAM_ENABLE_LOCAL_WIDGET.trim().toLowerCase();
  if (!configuredValue) {
    return import.meta.env.DEV;
  }

  return configuredValue === 'true';
};
