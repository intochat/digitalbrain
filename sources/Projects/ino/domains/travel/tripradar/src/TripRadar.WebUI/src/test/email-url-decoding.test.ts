import { describe, expect, it } from 'vitest';
import { fixEmailFromUrlParam, getEmailFromUrlParams } from 'shared/lib/url-utils';

/**
 * Test email URL decoding functionality
 * This tests the fix for the issue where + symbols in email addresses
 * were being converted to spaces when extracted from URL parameters
 */
describe('Email URL Decoding', () => {
  describe('fixEmailFromUrlParam', () => {
    it('should correctly decode email with + symbol from URL parameter', () => {
      // Simulate what happens when email with + is in URL
      const emailWithPlus = 'alexbardjo+user10@gmail.com';

      // This is what searchParams.get() returns (+ becomes space)
      const urlDecodedEmail = 'alexbardjo user10@gmail.com';

      // Our fix should work
      const fixedEmail = fixEmailFromUrlParam(urlDecodedEmail);
      expect(fixedEmail).toBe(emailWithPlus);

      // This should NOT work (demonstrates the problem)
      expect(urlDecodedEmail).not.toBe(emailWithPlus);
      expect(urlDecodedEmail).toBe('alexbardjo user10@gmail.com');
    });

    it('should handle normal emails without + symbol correctly', () => {
      const normalEmail = 'user@example.com';

      // Normal emails should work fine
      const fixedEmail = fixEmailFromUrlParam(normalEmail);
      expect(fixedEmail).toBe(normalEmail);
    });

    it('should handle emails with multiple + symbols', () => {
      const emailWithMultiplePlus = 'test+user+123@example.com';
      const urlDecodedEmail = 'test user 123@example.com'; // What searchParams.get() returns

      const fixedEmail = fixEmailFromUrlParam(urlDecodedEmail);
      expect(fixedEmail).toBe(emailWithMultiplePlus);
    });

    it('should handle emails with other special characters', () => {
      // Test various special characters that might appear in emails
      const testCases = [
        { original: 'user%40domain.com', expected: 'user%40domain.com' }, // % should stay as is
        { original: 'user@domain.com', expected: 'user@domain.com' }, // @ should stay as is
        { original: 'user.name@domain.com', expected: 'user.name@domain.com' }, // . should stay as is
        { original: 'user-name@domain.com', expected: 'user-name@domain.com' }, // - should stay as is
        { original: 'user_name@domain.com', expected: 'user_name@domain.com' }, // _ should stay as is
      ];

      testCases.forEach(({ original, expected }) => {
        const fixedEmail = fixEmailFromUrlParam(original);
        expect(fixedEmail).toBe(expected);
      });
    });

    it('should handle complex email with mixed special characters', () => {
      // Real-world example with + and other characters
      const originalEmail = 'test.user+tag123@sub-domain.example.com';
      const urlDecodedEmail = 'test.user tag123@sub-domain.example.com'; // Only + becomes space

      const fixedEmail = fixEmailFromUrlParam(urlDecodedEmail);
      expect(fixedEmail).toBe(originalEmail);
    });

    it('should return null for null input', () => {
      const result = fixEmailFromUrlParam(null);
      expect(result).toBeNull();
    });
  });

  describe('getEmailFromUrlParams', () => {
    it('should extract and fix email from URLSearchParams', () => {
      const searchParams = new URLSearchParams('?email=user tag@example.com');
      const result = getEmailFromUrlParams(searchParams);
      expect(result).toBe('user+tag@example.com');
    });

    it('should return null when email parameter is not present', () => {
      const searchParams = new URLSearchParams('?other=value');
      const result = getEmailFromUrlParams(searchParams);
      expect(result).toBeNull();
    });

    it('should work with custom parameter name', () => {
      const searchParams = new URLSearchParams('?userEmail=test user@example.com');
      const result = getEmailFromUrlParams(searchParams, 'userEmail');
      expect(result).toBe('test+user@example.com');
    });
  });
});
