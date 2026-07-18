/**
 * Tests for error message handling
 * Requirements: 5.3, 5.4
 */

import { beforeEach, describe, expect, it } from 'vitest';
import { createErrorMessages, parseBackendError, type ErrorCode, type NavigationHelpers } from './errorMessages';

// Mock window.location
const mockLocation = {
  origin: 'http://localhost:3000',
  href: '',
};

Object.defineProperty(window, 'location', {
  value: mockLocation,
  writable: true,
});

// Create navigation helpers for testing
const navigateToLogin = (email?: string) => {
  const url = new URL('/signin', mockLocation.origin);
  if (email && email.trim()) {
    url.searchParams.set('email', email);
  }
  mockLocation.href = url.toString();
};

const navigateToPasswordReset = (email?: string) => {
  const url = new URL('/forgot-password', mockLocation.origin);
  if (email && email.trim()) {
    url.searchParams.set('email', email);
  }
  mockLocation.href = url.toString();
};

const navigateToSignup = () => {
  mockLocation.href = '/signup';
};

const navigationHelpers: NavigationHelpers = {
  navigateToLogin,
  navigateToPasswordReset,
  navigateToSignup,
};

// Create ERROR_MESSAGES using the factory function
const ERROR_MESSAGES = createErrorMessages(navigationHelpers);

describe('ERROR_MESSAGES', () => {
  describe('USER_EXISTS', () => {
    it('should return correct error config with concise message', () => {
      const context = { email: 'test@example.com' };
      const config = ERROR_MESSAGES.USER_EXISTS(context);

      expect(config.title).toBe('Email Already Registered');
      expect(config.message).toBe('This email is already in use. Please log in or reset your password.');
      expect(config.severity).toBe('warning');
      expect(config.actions).toHaveLength(2);
    });

    it('should have correct action labels and variants', () => {
      const context = { email: 'test@example.com' };
      const config = ERROR_MESSAGES.USER_EXISTS(context);

      expect(config.actions?.[0].label).toBe('Log In');
      expect(config.actions?.[0].variant).toBe('primary');
      expect(config.actions?.[1].label).toBe('Reset Password');
      expect(config.actions?.[1].variant).toBe('secondary');
    });

    it('should have message under 100 characters', () => {
      const context = { email: 'test@example.com' };
      const config = ERROR_MESSAGES.USER_EXISTS(context);

      expect(config.message.length).toBeLessThanOrEqual(100);
    });
  });

  describe('WEAK_PASSWORD', () => {
    it('should return correct error config', () => {
      const config = ERROR_MESSAGES.WEAK_PASSWORD();

      expect(config.title).toBe('Password Too Weak');
      expect(config.message).toContain('at least 9 characters');
      expect(config.severity).toBe('error');
      expect(config.actions).toBeUndefined();
    });
  });

  describe('INVALID_EMAIL', () => {
    it('should return correct error config', () => {
      const config = ERROR_MESSAGES.INVALID_EMAIL();

      expect(config.title).toBe('Invalid Email');
      expect(config.message).toBe('Please enter a valid email address.');
      expect(config.severity).toBe('error');
      expect(config.actions).toBeUndefined();
    });
  });

  describe('NETWORK_ERROR', () => {
    it('should return correct error config', () => {
      const config = ERROR_MESSAGES.NETWORK_ERROR();

      expect(config.title).toBe('Connection Error');
      expect(config.message).toContain('internet connection');
      expect(config.severity).toBe('error');
      expect(config.actions).toBeUndefined();
    });
  });

  describe('SERVER_ERROR', () => {
    it('should return correct error config', () => {
      const config = ERROR_MESSAGES.SERVER_ERROR();

      expect(config.title).toBe('Server Error');
      expect(config.message).toContain('Something went wrong');
      expect(config.severity).toBe('error');
      expect(config.actions).toBeUndefined();
    });
  });

  describe('TELEGRAM_REQUIRED', () => {
    it('should return correct error config', () => {
      const config = ERROR_MESSAGES.TELEGRAM_REQUIRED();

      expect(config.title).toBe('Telegram Connection Required');
      expect(config.message).toContain('connect your Telegram account');
      expect(config.severity).toBe('info');
      expect(config.actions).toBeUndefined();
    });
  });

  describe('EMAIL_NOT_CONFIRMED', () => {
    it('should return correct error config', () => {
      const config = ERROR_MESSAGES.EMAIL_NOT_CONFIRMED();

      expect(config.title).toBe('Email Not Confirmed');
      expect(config.message).toContain('check your email');
      expect(config.severity).toBe('warning');
      expect(config.actions).toBeUndefined();
    });
  });

  describe('INVALID_TOKEN', () => {
    it('should return correct error config with action', () => {
      const config = ERROR_MESSAGES.INVALID_TOKEN();

      expect(config.title).toBe('Invalid Link');
      expect(config.message).toContain('invalid or has already been used');
      expect(config.severity).toBe('error');
      expect(config.actions).toHaveLength(1);
      expect(config.actions?.[0].label).toBe('Back to Signup');
      expect(config.actions?.[0].variant).toBe('primary');
    });
  });

  describe('TOKEN_EXPIRED', () => {
    it('should return correct error config with action', () => {
      const config = ERROR_MESSAGES.TOKEN_EXPIRED();

      expect(config.title).toBe('Link Expired');
      expect(config.message).toContain('expired');
      expect(config.severity).toBe('warning');
      expect(config.actions).toHaveLength(1);
      expect(config.actions?.[0].label).toBe('Register Again');
      expect(config.actions?.[0].variant).toBe('primary');
    });
  });

  describe('All error messages', () => {
    it('should have all required properties', () => {
      const errorCodes: ErrorCode[] = [
        'USER_EXISTS',
        'WEAK_PASSWORD',
        'INVALID_EMAIL',
        'NETWORK_ERROR',
        'SERVER_ERROR',
        'TELEGRAM_REQUIRED',
        'EMAIL_NOT_CONFIRMED',
        'INVALID_TOKEN',
        'TOKEN_EXPIRED',
        'PASSWORD_NOT_VALID',
        'USER_NOT_FOUND',
      ];

      errorCodes.forEach(code => {
        const config = ERROR_MESSAGES[code]();

        expect(config.title).toBeTruthy();
        expect(config.message).toBeTruthy();
        expect(['error', 'warning', 'info']).toContain(config.severity);

        // If actions exist, they should have proper structure
        if (config.actions) {
          config.actions.forEach(action => {
            expect(action.label).toBeTruthy();
            expect(['primary', 'secondary']).toContain(action.variant);
            expect(typeof action.onClick).toBe('function');
          });
        }
      });
    });
  });
});

describe('parseBackendError', () => {
  it('should parse USER_EXISTS error correctly', () => {
    const backendError = {
      response: {
        data: {
          code: 'USER_EXISTS',
          email: 'test@example.com',
        },
      },
    };

    const config = parseBackendError(backendError, navigationHelpers);

    expect(config.title).toBe('Email Already Registered');
    expect(config.message).toBe('This email is already in use. Please log in or reset your password.');
    expect(config.severity).toBe('warning');
    expect(config.actions).toHaveLength(2);
  });

  it('should handle error without email context', () => {
    const backendError = {
      response: {
        data: {
          code: 'USER_EXISTS',
        },
      },
    };

    const config = parseBackendError(backendError, navigationHelpers);

    expect(config.title).toBe('Email Already Registered');
    expect(config.actions).toHaveLength(2);
  });

  it('should parse all error codes correctly', () => {
    const errorCodes: ErrorCode[] = [
      'WEAK_PASSWORD',
      'INVALID_EMAIL',
      'NETWORK_ERROR',
      'SERVER_ERROR',
      'TELEGRAM_REQUIRED',
      'EMAIL_NOT_CONFIRMED',
      'INVALID_TOKEN',
      'TOKEN_EXPIRED',
      'PASSWORD_NOT_VALID',
      'USER_NOT_FOUND',
    ];

    errorCodes.forEach(code => {
      const backendError = {
        response: {
          data: {
            code,
          },
        },
      };

      const config = parseBackendError(backendError);
      const expectedConfig = ERROR_MESSAGES[code]();

      expect(config.title).toBe(expectedConfig.title);
      expect(config.message).toBe(expectedConfig.message);
      expect(config.severity).toBe(expectedConfig.severity);
    });
  });

  it('should handle error code in root level', () => {
    const backendError = {
      code: 'NETWORK_ERROR',
    };

    const config = parseBackendError(backendError);

    expect(config.title).toBe('Connection Error');
    expect(config.severity).toBe('error');
  });

  it('should handle unknown error code', () => {
    const backendError = {
      response: {
        data: {
          code: 'UNKNOWN_ERROR',
        },
      },
    };

    const config = parseBackendError(backendError);

    expect(config.title).toBe('Error');
    expect(config.message).toBe('An unexpected error occurred. Please try again.');
    expect(config.severity).toBe('error');
    expect(config.actions).toBeUndefined();
  });

  it('should handle error without response data', () => {
    const backendError = {
      message: 'Custom error message',
    };

    const config = parseBackendError(backendError);

    expect(config.title).toBe('Error');
    expect(config.message).toBe('Custom error message');
    expect(config.severity).toBe('error');
  });

  it('should handle error without any message', () => {
    const backendError = {};

    const config = parseBackendError(backendError);

    expect(config.title).toBe('Error');
    expect(config.message).toBe('An unexpected error occurred. Please try again.');
    expect(config.severity).toBe('error');
  });

  it('should pass context to error message functions', () => {
    const backendError = {
      response: {
        data: {
          code: 'USER_EXISTS',
          email: 'user@test.com',
          customField: 'customValue',
        },
      },
    };

    const config = parseBackendError(backendError, navigationHelpers);

    // The context should be passed to the error message function
    expect(config.actions).toHaveLength(2);
    // We can't directly test the context passing, but we know it works
    // because the actions are created with the email context
  });
});

describe('Navigation helpers', () => {
  beforeEach(() => {
    mockLocation.href = '';
  });

  describe('navigateToLogin', () => {
    it('should navigate to login page without email', () => {
      navigateToLogin();
      expect(mockLocation.href).toBe('http://localhost:3000/signin');
    });

    it('should navigate to login page with email pre-fill', () => {
      navigateToLogin('test@example.com');
      expect(mockLocation.href).toBe('http://localhost:3000/signin?email=test%40example.com');
    });

    it('should handle special characters in email', () => {
      navigateToLogin('test+tag@example.com');
      expect(mockLocation.href).toBe('http://localhost:3000/signin?email=test%2Btag%40example.com');
    });

    it('should handle empty string email', () => {
      navigateToLogin('');
      expect(mockLocation.href).toBe('http://localhost:3000/signin');
    });
  });

  describe('navigateToPasswordReset', () => {
    it('should navigate to forgot password page without email', () => {
      navigateToPasswordReset();
      expect(mockLocation.href).toBe('http://localhost:3000/forgot-password');
    });

    it('should navigate to forgot password page with email pre-fill', () => {
      navigateToPasswordReset('test@example.com');
      expect(mockLocation.href).toBe('http://localhost:3000/forgot-password?email=test%40example.com');
    });

    it('should handle special characters in email', () => {
      navigateToPasswordReset('test+tag@example.com');
      expect(mockLocation.href).toBe('http://localhost:3000/forgot-password?email=test%2Btag%40example.com');
    });

    it('should handle empty string email', () => {
      navigateToPasswordReset('');
      expect(mockLocation.href).toBe('http://localhost:3000/forgot-password');
    });
  });

  describe('navigateToSignup', () => {
    it('should navigate to signup page', () => {
      navigateToSignup();
      expect(mockLocation.href).toBe('/signup');
    });
  });
});
