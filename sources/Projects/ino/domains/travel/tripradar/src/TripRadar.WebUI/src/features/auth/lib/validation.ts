/**
 * Password validation utilities for registration
 * Requirements: 1.1, 1.2, 1.3, 1.4, 1.5
 */

export interface PasswordValidationResult {
  isValid: boolean;
  errors: string[];
}

export interface PasswordRequirements {
  minLength: boolean;
  hasUppercase: boolean;
  hasDigit: boolean;
  hasSpecialChar: boolean;
}

const MIN_PASSWORD_LENGTH = 9;
const SPECIAL_CHARS_REGEX = /[!@#$%^&*()_+\-=[\]{}|;:,.<>?]/;
const UPPERCASE_REGEX = /[A-Z]/;
const DIGIT_REGEX = /\d/;

/**
 * Validate password against all requirements
 * @param password - The password to validate
 * @returns Validation result with isValid flag and error messages
 */
export const validatePassword = (password: string): PasswordValidationResult => {
  const errors: string[] = [];

  if (password.length < MIN_PASSWORD_LENGTH) {
    errors.push(`Password must be at least ${MIN_PASSWORD_LENGTH} characters long`);
  }

  if (!UPPERCASE_REGEX.test(password)) {
    errors.push('Password must contain at least one uppercase letter');
  }

  if (!DIGIT_REGEX.test(password)) {
    errors.push('Password must contain at least one digit');
  }

  if (!SPECIAL_CHARS_REGEX.test(password)) {
    errors.push('Password must contain at least one special character');
  }

  return {
    isValid: errors.length === 0,
    errors,
  };
};

/**
 * Check individual password requirements (for UI indicators)
 * @param password - The password to check
 * @returns Object with boolean flags for each requirement
 */
export const checkPasswordRequirements = (password: string): PasswordRequirements => {
  return {
    minLength: password.length >= MIN_PASSWORD_LENGTH,
    hasUppercase: UPPERCASE_REGEX.test(password),
    hasDigit: DIGIT_REGEX.test(password),
    hasSpecialChar: SPECIAL_CHARS_REGEX.test(password),
  };
};
