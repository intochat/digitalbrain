import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import type { TelegramData } from 'shared/api/types';
import { getTelegramBotUsername, loadTelegramWidget, validateTelegramData } from './telegram';

describe('Telegram Utilities', () => {
  describe('validateTelegramData', () => {
    it('should return true for valid TelegramData with all required fields', () => {
      const validData: TelegramData = {
        id: 123456789,
        first_name: 'John',
        auth_date: 1234567890,
        hash: 'abc123def456',
      };

      expect(validateTelegramData(validData)).toBe(true);
    });

    it('should return true for valid TelegramData with optional fields', () => {
      const validData: TelegramData = {
        id: 123456789,
        first_name: 'John',
        last_name: 'Doe',
        username: 'johndoe',
        photo_url: 'https://example.com/photo.jpg',
        auth_date: 1234567890,
        hash: 'abc123def456',
      };

      expect(validateTelegramData(validData)).toBe(true);
    });

    it('should return false for null or undefined', () => {
      expect(validateTelegramData(null)).toBe(false);
      expect(validateTelegramData(undefined)).toBe(false);
    });

    it('should return false for non-object types', () => {
      expect(validateTelegramData('string')).toBe(false);
      expect(validateTelegramData(123)).toBe(false);
      expect(validateTelegramData(true)).toBe(false);
    });

    it('should return false when missing required id field', () => {
      const invalidData = {
        first_name: 'John',
        auth_date: 1234567890,
        hash: 'abc123def456',
      };

      expect(validateTelegramData(invalidData)).toBe(false);
    });

    it('should return false when missing required first_name field', () => {
      const invalidData = {
        id: 123456789,
        auth_date: 1234567890,
        hash: 'abc123def456',
      };

      expect(validateTelegramData(invalidData)).toBe(false);
    });

    it('should return false when missing required auth_date field', () => {
      const invalidData = {
        id: 123456789,
        first_name: 'John',
        hash: 'abc123def456',
      };

      expect(validateTelegramData(invalidData)).toBe(false);
    });

    it('should return false when missing required hash field', () => {
      const invalidData = {
        id: 123456789,
        first_name: 'John',
        auth_date: 1234567890,
      };

      expect(validateTelegramData(invalidData)).toBe(false);
    });

    it('should return false when id is not a number', () => {
      const invalidData = {
        id: '123456789',
        first_name: 'John',
        auth_date: 1234567890,
        hash: 'abc123def456',
      };

      expect(validateTelegramData(invalidData)).toBe(false);
    });

    it('should return false when first_name is not a string', () => {
      const invalidData = {
        id: 123456789,
        first_name: 123,
        auth_date: 1234567890,
        hash: 'abc123def456',
      };

      expect(validateTelegramData(invalidData)).toBe(false);
    });

    it('should return false when optional field has wrong type', () => {
      const invalidData = {
        id: 123456789,
        first_name: 'John',
        username: 123, // Should be string
        auth_date: 1234567890,
        hash: 'abc123def456',
      };

      expect(validateTelegramData(invalidData)).toBe(false);
    });
  });

  describe('loadTelegramWidget', () => {
    beforeEach(() => {
      // Clean up any existing scripts
      document.querySelectorAll('script[src*="telegram-widget"]').forEach(el => el.remove());
    });

    afterEach(() => {
      // Clean up after tests
      document.querySelectorAll('script[src*="telegram-widget"]').forEach(el => el.remove());
    });

    it('should load the Telegram widget script', async () => {
      const loadPromise = loadTelegramWidget();

      // Find the script element that was added
      const script = document.querySelector('script[src*="telegram-widget"]') as HTMLScriptElement;
      expect(script).toBeTruthy();
      expect(script.src).toContain('telegram.org/js/telegram-widget.js');
      expect(script.async).toBe(true);

      // Simulate script load
      script.onload?.(new Event('load'));

      await expect(loadPromise).resolves.toBeUndefined();
    });

    it('should not load script twice if already loaded', async () => {
      // First load
      const firstLoad = loadTelegramWidget();
      const firstScript = document.querySelector('script[src*="telegram-widget"]');
      (firstScript as HTMLScriptElement).onload?.(new Event('load'));
      await firstLoad;

      // Second load should resolve immediately
      await expect(loadTelegramWidget()).resolves.toBeUndefined();

      // Should still only have one script
      const scripts = document.querySelectorAll('script[src*="telegram-widget"]');
      expect(scripts.length).toBe(1);
    });

    it('should reject if script fails to load', async () => {
      const loadPromise = loadTelegramWidget();

      const script = document.querySelector('script[src*="telegram-widget"]') as HTMLScriptElement;
      script.onerror?.(new Event('error'));

      await expect(loadPromise).rejects.toThrow('Failed to load Telegram widget script');
    });
  });

  describe('getTelegramBotUsername', () => {
    it('should return the bot username from environment variables', () => {
      // The function should return the value from VITE_TELEGRAM_BOT_USERNAME
      const username = getTelegramBotUsername();
      expect(typeof username).toBe('string');
      expect(username.length).toBeGreaterThan(0);
    });
  });
});
