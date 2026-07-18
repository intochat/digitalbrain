import type { ToastType } from 'shared/ui/ToastNotification';

export interface NotificationConfig {
  title: string;
  message?: string;
  type: ToastType;
  duration?: number;
}

export const PREFERENCE_NOTIFICATIONS = {
  SAVE_SUCCESS: {
    title: 'Preferences Saved',
    message: 'Your travel preferences have been updated successfully.',
    type: 'success' as ToastType,
    duration: 4000,
  },
  SAVE_ERROR: {
    title: 'Save Failed',
    message: 'Unable to save your preferences. Please try again.',
    type: 'error' as ToastType,
    duration: 6000,
  },
  NETWORK_ERROR: {
    title: 'Connection Error',
    message: 'Please check your internet connection and try again.',
    type: 'error' as ToastType,
    duration: 6000,
  },
  SERVER_ERROR: {
    title: 'Server Error',
    message: 'The server is temporarily unavailable. Please try again later.',
    type: 'error' as ToastType,
    duration: 6000,
  },
  VALIDATION_ERROR: {
    title: 'Validation Error',
    message: 'Please check your input and fix any errors before saving.',
    type: 'error' as ToastType,
    duration: 5000,
  },
  LOAD_ERROR: {
    title: 'Loading Failed',
    message: 'Unable to load your preferences. Using default values.',
    type: 'info' as ToastType,
    duration: 5000,
  },
  RETRY_SUCCESS: {
    title: 'Connection Restored',
    message: 'Your preferences have been loaded successfully.',
    type: 'success' as ToastType,
    duration: 3000,
  },
} as const;

export const getValidationErrorNotification = (errorCount: number): NotificationConfig => ({
  title: 'Validation Errors',
  message: `Please fix ${errorCount} error${errorCount > 1 ? 's' : ''} before saving.`,
  type: 'error',
  duration: 5000,
});

export const getCustomErrorNotification = (error: unknown): NotificationConfig => {
  const message =
    error && typeof error === 'object' && 'message' in error ? String(error.message) : 'An unexpected error occurred';

  return {
    title: 'Error',
    message,
    type: 'error',
    duration: 6000,
  };
};
