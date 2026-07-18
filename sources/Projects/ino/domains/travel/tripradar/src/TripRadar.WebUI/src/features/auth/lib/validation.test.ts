/**
 * Unit tests for password validation
 * Requirements: 1.1, 1.2, 1.3, 1.4
 */

import { describe, expect, it } from 'vitest';
import { checkPasswordRequirements, validatePassword } from './validation';

describe('validatePassword', () => {
  describe('minimum length requirement (9 chars)', () => {
    it('should reject passwords shorter than 9 characters', () => {
      const result = validatePassword('Short1!');
      expect(result.isValid).toBe(false);
      expect(result.errors).toContain('Password must be at least 9 characters long');
    });

    it('should accept passwords with exactly 9 characters', () => {
      const result = validatePassword('Valid123!');
      expect(result.isValid).toBe(true);
      expect(result.errors).toHaveLength(0);
    });

    it('should accept passwords longer than 9 characters', () => {
      const result = validatePassword('VeryLongPassword123!');
      expect(result.isValid).toBe(true);
      expect(result.errors).toHaveLength(0);
    });
  });

  describe('uppercase letter requirement', () => {
    it('should reject passwords without uppercase letters', () => {
      const result = validatePassword('lowercase123!');
      expect(result.isValid).toBe(false);
      expect(result.errors).toContain('Password must contain at least one uppercase letter');
    });

    it('should accept passwords with uppercase letters', () => {
      const result = validatePassword('Uppercase123!');
      expect(result.isValid).toBe(true);
      expect(result.errors).toHaveLength(0);
    });

    it('should accept passwords with multiple uppercase letters', () => {
      const result = validatePassword('UPPERCASE123!');
      expect(result.isValid).toBe(true);
      expect(result.errors).toHaveLength(0);
    });
  });

  describe('digit requirement', () => {
    it('should reject passwords without digits', () => {
      const result = validatePassword('NoDigits!');
      expect(result.isValid).toBe(false);
      expect(result.errors).toContain('Password must contain at least one digit');
    });

    it('should accept passwords with digits', () => {
      const result = validatePassword('WithDigit1!');
      expect(result.isValid).toBe(true);
      expect(result.errors).toHaveLength(0);
    });

    it('should accept passwords with multiple digits', () => {
      const result = validatePassword('Multiple123!');
      expect(result.isValid).toBe(true);
      expect(result.errors).toHaveLength(0);
    });
  });

  describe('special character requirement', () => {
    it('should reject passwords without special characters', () => {
      const result = validatePassword('NoSpecial123');
      expect(result.isValid).toBe(false);
      expect(result.errors).toContain('Password must contain at least one special character');
    });

    it('should accept passwords with common special characters', () => {
      const specialChars = ['!', '@', '#', '$', '%', '^', '&', '*', '(', ')', '_', '+', '-', '='];

      specialChars.forEach(char => {
        const result = validatePassword(`Password1${char}`);
        expect(result.isValid).toBe(true);
        expect(result.errors).toHaveLength(0);
      });
    });

    it('should accept passwords with brackets and braces', () => {
      const brackets = ['[', ']', '{', '}', '|', ';', ':', ',', '.', '<', '>', '?'];

      brackets.forEach(char => {
        const result = validatePassword(`Password1${char}`);
        expect(result.isValid).toBe(true);
        expect(result.errors).toHaveLength(0);
      });
    });
  });

  describe('valid password acceptance', () => {
    it('should accept valid passwords with all requirements', () => {
      const validPasswords = ['ValidPass123!', 'MyP@ssw0rd', 'Secure#Pass1', 'Test$1234', 'Complex&Pass9'];

      validPasswords.forEach(password => {
        const result = validatePassword(password);
        expect(result.isValid).toBe(true);
        expect(result.errors).toHaveLength(0);
      });
    });
  });

  describe('multiple simultaneous failures', () => {
    it('should return all errors for completely invalid password', () => {
      const result = validatePassword('short');
      expect(result.isValid).toBe(false);
      expect(result.errors).toHaveLength(4);
      expect(result.errors).toContain('Password must be at least 9 characters long');
      expect(result.errors).toContain('Password must contain at least one uppercase letter');
      expect(result.errors).toContain('Password must contain at least one digit');
      expect(result.errors).toContain('Password must contain at least one special character');
    });

    it('should return multiple errors for partially invalid password', () => {
      const result = validatePassword('nouppercase1!');
      expect(result.isValid).toBe(false);
      expect(result.errors).toHaveLength(1);
      expect(result.errors).toContain('Password must contain at least one uppercase letter');
    });

    it('should return two errors when missing uppercase and digit', () => {
      const result = validatePassword('nouppernumber!');
      expect(result.isValid).toBe(false);
      expect(result.errors).toHaveLength(2);
      expect(result.errors).toContain('Password must contain at least one uppercase letter');
      expect(result.errors).toContain('Password must contain at least one digit');
    });
  });

  describe('edge cases', () => {
    it('should handle empty string', () => {
      const result = validatePassword('');
      expect(result.isValid).toBe(false);
      expect(result.errors.length).toBeGreaterThan(0);
    });

    it('should handle whitespace-only password', () => {
      const result = validatePassword('         ');
      expect(result.isValid).toBe(false);
    });

    it('should accept password with spaces if it meets requirements', () => {
      const result = validatePassword('My Pass123!');
      expect(result.isValid).toBe(true);
      expect(result.errors).toHaveLength(0);
    });

    it('should handle very long passwords', () => {
      const veryLongPassword = 'A'.repeat(1000) + '1!';
      const result = validatePassword(veryLongPassword);
      expect(result.isValid).toBe(true);
      expect(result.errors).toHaveLength(0);
    });

    it('should handle unicode characters in passwords', () => {
      const unicodePassword = 'Pässwörd123!';
      const result = validatePassword(unicodePassword);
      expect(result.isValid).toBe(true);
      expect(result.errors).toHaveLength(0);
    });

    it('should handle unicode uppercase characters (ASCII uppercase required)', () => {
      const unicodePassword = 'pÄssword123!';
      const result = validatePassword(unicodePassword);
      expect(result.isValid).toBe(false); // Ä is not ASCII uppercase
      expect(result.errors).toContain('Password must contain at least one uppercase letter');
    });

    it('should handle unicode special characters', () => {
      const unicodePassword = 'Password123€';
      const result = validatePassword(unicodePassword);
      expect(result.isValid).toBe(false); // € is not in our special chars regex
      expect(result.errors).toContain('Password must contain at least one special character');
    });

    it('should handle mixed unicode and ASCII', () => {
      const mixedPassword = 'Pässwörd123!';
      const result = validatePassword(mixedPassword);
      expect(result.isValid).toBe(true);
      expect(result.errors).toHaveLength(0);
    });
  });
});

describe('checkPasswordRequirements', () => {
  it('should return false for all requirements with empty password', () => {
    const result = checkPasswordRequirements('');
    expect(result.minLength).toBe(false);
    expect(result.hasUppercase).toBe(false);
    expect(result.hasDigit).toBe(false);
    expect(result.hasSpecialChar).toBe(false);
  });

  it('should return true only for met requirements', () => {
    const result = checkPasswordRequirements('Short1!');
    expect(result.minLength).toBe(false); // Only 7 chars
    expect(result.hasUppercase).toBe(true);
    expect(result.hasDigit).toBe(true);
    expect(result.hasSpecialChar).toBe(true);
  });

  it('should return true for all requirements with valid password', () => {
    const result = checkPasswordRequirements('ValidPass123!');
    expect(result.minLength).toBe(true);
    expect(result.hasUppercase).toBe(true);
    expect(result.hasDigit).toBe(true);
    expect(result.hasSpecialChar).toBe(true);
  });

  it('should correctly identify missing uppercase', () => {
    const result = checkPasswordRequirements('lowercase123!');
    expect(result.hasUppercase).toBe(false);
    expect(result.minLength).toBe(true);
    expect(result.hasDigit).toBe(true);
    expect(result.hasSpecialChar).toBe(true);
  });

  it('should correctly identify missing digit', () => {
    const result = checkPasswordRequirements('NoDigits!');
    expect(result.hasDigit).toBe(false);
    expect(result.minLength).toBe(true); // 9 chars exactly
    expect(result.hasUppercase).toBe(true);
    expect(result.hasSpecialChar).toBe(true);
  });

  it('should correctly identify missing special character', () => {
    const result = checkPasswordRequirements('NoSpecial123');
    expect(result.hasSpecialChar).toBe(false);
    expect(result.minLength).toBe(true);
    expect(result.hasUppercase).toBe(true);
    expect(result.hasDigit).toBe(true);
  });

  describe('edge cases for checkPasswordRequirements', () => {
    it('should handle very long passwords', () => {
      const veryLongPassword = 'A'.repeat(1000) + '1!';
      const result = checkPasswordRequirements(veryLongPassword);
      expect(result.minLength).toBe(true);
      expect(result.hasUppercase).toBe(true);
      expect(result.hasDigit).toBe(true);
      expect(result.hasSpecialChar).toBe(true);
    });

    it('should handle unicode characters', () => {
      const unicodePassword = 'Pässwörd123!';
      const result = checkPasswordRequirements(unicodePassword);
      expect(result.minLength).toBe(true);
      expect(result.hasUppercase).toBe(true);
      expect(result.hasDigit).toBe(true);
      expect(result.hasSpecialChar).toBe(true);
    });

    it('should handle whitespace-only password', () => {
      const result = checkPasswordRequirements('         ');
      expect(result.minLength).toBe(true); // 9 spaces
      expect(result.hasUppercase).toBe(false);
      expect(result.hasDigit).toBe(false);
      expect(result.hasSpecialChar).toBe(false);
    });

    it('should handle password with only numbers', () => {
      const result = checkPasswordRequirements('123456789');
      expect(result.minLength).toBe(true);
      expect(result.hasUppercase).toBe(false);
      expect(result.hasDigit).toBe(true);
      expect(result.hasSpecialChar).toBe(false);
    });

    it('should handle password with only special characters', () => {
      const result = checkPasswordRequirements('!@#$%^&*()');
      expect(result.minLength).toBe(true);
      expect(result.hasUppercase).toBe(false);
      expect(result.hasDigit).toBe(false);
      expect(result.hasSpecialChar).toBe(true);
    });

    it('should handle password with only uppercase letters', () => {
      const result = checkPasswordRequirements('ABCDEFGHI');
      expect(result.minLength).toBe(true);
      expect(result.hasUppercase).toBe(true);
      expect(result.hasDigit).toBe(false);
      expect(result.hasSpecialChar).toBe(false);
    });
  });
});
