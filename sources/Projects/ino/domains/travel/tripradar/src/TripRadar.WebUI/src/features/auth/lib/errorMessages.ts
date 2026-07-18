/**
 * Error message mapping and handling utilities
 * Requirements: 4.1, 4.2, 4.3, 4.4, 4.5
 */
import { frontendI18n } from 'app/i18n';

export interface ErrorAction {
  label: string;
  onClick: () => void;
  variant: 'primary' | 'secondary';
}

export interface NavigationHelpers {
  navigateToLogin: (email?: string) => void;
  navigateToSignup: () => void;
  navigateToPasswordReset: (email?: string) => void;
}

export interface ErrorConfig {
  title: string;
  message: string;
  actions?: ErrorAction[];
  severity: 'error' | 'warning' | 'info';
}

export type ErrorCode =
  | 'USER_EXISTS'
  | 'WEAK_PASSWORD'
  | 'INVALID_EMAIL'
  | 'NETWORK_ERROR'
  | 'SERVER_ERROR'
  | 'TELEGRAM_REQUIRED'
  | 'TELEGRAM_ACCOUNT_NOT_LINKED'
  | 'EMAIL_NOT_CONFIRMED'
  | 'INVALID_TOKEN'
  | 'TOKEN_EXPIRED'
  | 'PASSWORD_NOT_VALID'
  | 'USERNAME_OR_PASSWORD_NOT_VALID'
  | 'USER_NOT_FOUND';

interface ErrorContext {
  email?: string;
  [key: string]: unknown;
}

/**
 * Error message mapping for all error codes
 */
export const createErrorMessages = (
  navigation?: NavigationHelpers
): Record<ErrorCode, (context?: ErrorContext) => ErrorConfig> => ({
  USER_EXISTS: context => ({
    title: frontendI18n.t('Email Already Registered'),
    message: frontendI18n.t('This email is already in use. Please log in or reset your password.'),
    severity: 'warning',
    actions: navigation
      ? [
          {
            label: frontendI18n.t('Log In'),
            onClick: () => navigation.navigateToLogin(context?.email),
            variant: 'primary',
          },
          {
            label: frontendI18n.t('Reset Password'),
            onClick: () => navigation.navigateToPasswordReset(context?.email),
            variant: 'secondary',
          },
        ]
      : [],
  }),

  WEAK_PASSWORD: () => ({
    title: frontendI18n.t('Password Too Weak'),
    message: frontendI18n.t(
      'Your password must be at least 9 characters and include an uppercase letter, a digit, and a special character.'
    ),
    severity: 'error',
  }),

  INVALID_EMAIL: () => ({
    title: frontendI18n.t('Invalid Email'),
    message: frontendI18n.t('Please enter a valid email address.'),
    severity: 'error',
  }),

  NETWORK_ERROR: () => ({
    title: frontendI18n.t('Connection Error'),
    message: frontendI18n.t('Unable to connect to the server. Please check your internet connection and try again.'),
    severity: 'error',
  }),

  SERVER_ERROR: () => ({
    title: frontendI18n.t('Server Error'),
    message: frontendI18n.t('Something went wrong on our end. Please try again in a few moments.'),
    severity: 'error',
  }),

  TELEGRAM_REQUIRED: () => ({
    title: frontendI18n.t('Telegram Connection Required'),
    message: frontendI18n.t('Please connect your Telegram account to complete registration.'),
    severity: 'info',
  }),

  EMAIL_NOT_CONFIRMED: () => ({
    title: frontendI18n.t('Email Not Confirmed'),
    message: frontendI18n.t('Please check your email and click the confirmation link before logging in.'),
    severity: 'warning',
  }),

  INVALID_TOKEN: () => ({
    title: frontendI18n.t('Invalid Link'),
    message: frontendI18n.t('This confirmation link is invalid or has already been used.'),
    severity: 'error',
    actions: navigation
      ? [
          {
            label: frontendI18n.t('Back to Signup'),
            onClick: () => navigation.navigateToSignup(),
            variant: 'primary',
          },
        ]
      : [],
  }),

  TOKEN_EXPIRED: () => ({
    title: frontendI18n.t('Link Expired'),
    message: frontendI18n.t('This confirmation link has expired. Please register again to receive a new link.'),
    severity: 'warning',
    actions: navigation
      ? [
          {
            label: frontendI18n.t('Register Again'),
            onClick: () => navigation.navigateToSignup(),
            variant: 'primary',
          },
        ]
      : [],
  }),

  PASSWORD_NOT_VALID: context => ({
    title: frontendI18n.t('Invalid Password'),
    message: frontendI18n.t('The password you entered is incorrect. Please try again.'),
    severity: 'error',
    actions: navigation
      ? [
          {
            label: frontendI18n.t('Reset Password'),
            onClick: () => navigation.navigateToPasswordReset(context?.email),
            variant: 'secondary',
          },
        ]
      : [],
  }),

  USERNAME_OR_PASSWORD_NOT_VALID: context => ({
    title: frontendI18n.t('Invalid Credentials'),
    message: frontendI18n.t('The email or password you entered is incorrect. Please try again.'),
    severity: 'error',
    actions: navigation
      ? [
          {
            label: frontendI18n.t('Reset Password'),
            onClick: () => navigation.navigateToPasswordReset(context?.email),
            variant: 'secondary',
          },
        ]
      : [],
  }),

  USER_NOT_FOUND: () => ({
    title: frontendI18n.t('Account Not Found'),
    message: frontendI18n.t(
      'No account found with this email address. Please check your email or create a new account.'
    ),
    severity: 'error',
    actions: navigation
      ? [
          {
            label: frontendI18n.t('Create Account'),
            onClick: () => navigation.navigateToSignup(),
            variant: 'primary',
          },
        ]
      : [],
  }),
});

interface BackendError {
  response?: {
    data?: {
      code?: string;
      errorCode?: string;
      [key: string]: unknown;
    };
  };
  code?: string;
  message?: string;
}

/**
 * Parse backend error and return user-friendly error config
 * @param error - Error object from API call
 * @param navigation - Navigation helpers for SPA routing
 * @returns ErrorConfig with title, message, severity, and optional actions
 */
export const parseBackendError = (error: BackendError, navigation?: NavigationHelpers): ErrorConfig => {
  // Try to extract error code from response (check both 'code' and 'errorCode' fields)
  const errorCode = error?.response?.data?.code || error?.response?.data?.errorCode || error?.code;

  // Handle ASP.NET Core validation errors
  if (!errorCode && error?.response?.data?.errors) {
    const validationErrors = error.response.data.errors as Record<string, string[]>;

    // Extract first validation error message
    const firstErrorKey = Object.keys(validationErrors)[0];
    const firstErrorMessage = validationErrors[firstErrorKey]?.[0];

    return {
      title: frontendI18n.t('Validation Error'),
      message: firstErrorMessage || frontendI18n.t('Please check your input and try again.'),
      severity: 'error',
    };
  }

  // Check if we have a mapping for this error code
  const errorMessages = createErrorMessages(navigation);
  if (errorCode && errorMessages[errorCode as ErrorCode]) {
    const context = (error?.response?.data || {}) as ErrorContext;
    return errorMessages[errorCode as ErrorCode](context);
  }

  // Fallback to generic error
  return {
    title: frontendI18n.t('Error'),
    message: error?.message || frontendI18n.t('An unexpected error occurred. Please try again.'),
    severity: 'error',
  };
};
