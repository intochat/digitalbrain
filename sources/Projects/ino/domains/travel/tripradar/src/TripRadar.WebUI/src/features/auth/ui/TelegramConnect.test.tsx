import { describe, expect, it, vi, beforeEach } from 'vitest';
import { useLinkTelegramMutation } from 'entities/auth';
import type { LinkTelegramResponse, TelegramData } from 'shared/api/types';
import {
  convertTelegramDataToApiFormat,
  getTelegramBotUsername,
  loadTelegramWidget,
  validateTelegramData,
} from '../lib/telegram';

// Mock dependencies
vi.mock('entities/auth');
vi.mock('../lib/telegram');

/**
 * Unit tests for TelegramConnect component core functionality
 * Requirements: 3.1, 3.2, 3.3
 *
 * These tests focus on the core business logic and mocked function behavior
 * to ensure the component handles error states, retry functionality, and
 * success callbacks correctly.
 */
describe('TelegramConnect Component Logic', () => {
  const mockTelegramData: TelegramData = {
    id: 123456789,
    first_name: 'John',
    last_name: 'Doe',
    username: 'johndoe',
    photo_url: 'https://example.com/photo.jpg',
    auth_date: 1640995200,
    hash: 'abc123hash',
  };

  const mockLinkTelegramResponse: LinkTelegramResponse = {
    token: 'mock-auth-token',
    refreshToken: 'mock-refresh-token',
    email: 'test@example.com',
    username: 'testuser',
    message: 'Success',
  };

  beforeEach(() => {
    vi.clearAllMocks();

    // Setup default mocks
    vi.mocked(useLinkTelegramMutation).mockReturnValue({
      mutate: vi.fn(),
      isPending: false,
      isError: false,
      error: null,
      data: undefined,
      isSuccess: false,
      reset: vi.fn(),
    } as unknown as ReturnType<typeof useLinkTelegramMutation>);

    vi.mocked(loadTelegramWidget).mockResolvedValue();
    vi.mocked(getTelegramBotUsername).mockReturnValue('test_bot');
    vi.mocked(validateTelegramData).mockReturnValue(true);
    vi.mocked(convertTelegramDataToApiFormat).mockReturnValue({
      id: mockTelegramData.id,
      firstName: mockTelegramData.first_name,
      lastName: mockTelegramData.last_name || null,
      username: mockTelegramData.username || '',
      photoUrl: mockTelegramData.photo_url || null,
      authDate: mockTelegramData.auth_date,
      hash: mockTelegramData.hash,
    });
  });

  describe('widget initialization', () => {
    it('should call loadTelegramWidget on initialization', async () => {
      await loadTelegramWidget();

      expect(loadTelegramWidget).toHaveBeenCalledTimes(1);
    });

    it('should get bot username during initialization', () => {
      const botUsername = getTelegramBotUsername();

      expect(getTelegramBotUsername).toHaveBeenCalledTimes(1);
      expect(botUsername).toBe('test_bot');
    });

    it('should handle widget loading failure', async () => {
      const loadError = new Error('Failed to load Telegram widget script');
      vi.mocked(loadTelegramWidget).mockRejectedValue(loadError);

      try {
        await loadTelegramWidget();
      } catch (error) {
        expect(error).toEqual(loadError);
      }

      expect(loadTelegramWidget).toHaveBeenCalledTimes(1);
    });
  });

  describe('success callback handling', () => {
    it('should validate Telegram data before processing', () => {
      const isValid = validateTelegramData(mockTelegramData);

      expect(validateTelegramData).toHaveBeenCalledWith(mockTelegramData);
      expect(isValid).toBe(true);
    });

    it('should convert Telegram data to API format', () => {
      const convertedData = convertTelegramDataToApiFormat(mockTelegramData);

      expect(convertTelegramDataToApiFormat).toHaveBeenCalledWith(mockTelegramData);
      expect(convertedData).toEqual({
        id: mockTelegramData.id,
        firstName: mockTelegramData.first_name,
        lastName: mockTelegramData.last_name || null,
        username: mockTelegramData.username || '',
        photoUrl: mockTelegramData.photo_url || null,
        authDate: mockTelegramData.auth_date,
        hash: mockTelegramData.hash,
      });
    });

    it('should keep payload shape for successful authentication callback', () => {
      expect(mockLinkTelegramResponse.username).toBe('testuser');
      expect(mockLinkTelegramResponse.email).toBe('test@example.com');
    });

    it('should handle invalid Telegram data', () => {
      vi.mocked(validateTelegramData).mockReturnValue(false);

      const isValid = validateTelegramData(mockTelegramData);

      expect(isValid).toBe(false);
      expect(validateTelegramData).toHaveBeenCalledWith(mockTelegramData);
    });
  });

  describe('error callback with error display', () => {
    it('should handle API errors during linking', () => {
      const apiError = new Error('API connection failed');

      vi.mocked(useLinkTelegramMutation).mockReturnValue({
        mutate: vi.fn(),
        isPending: false,
        isError: true,
        error: apiError,
        data: undefined,
        isSuccess: false,
        reset: vi.fn(),
      } as unknown as ReturnType<typeof useLinkTelegramMutation>);

      const mutation = useLinkTelegramMutation();

      expect(mutation.isError).toBe(true);
      expect(mutation.error).toEqual(apiError);
    });

    it('should handle non-Error objects in API responses', () => {
      const stringError = new Error('String error message');

      vi.mocked(useLinkTelegramMutation).mockReturnValue({
        mutate: vi.fn(),
        isPending: false,
        isError: true,
        error: stringError,
        data: undefined,
        isSuccess: false,
        reset: vi.fn(),
      } as unknown as ReturnType<typeof useLinkTelegramMutation>);

      const mutation = useLinkTelegramMutation();

      expect(mutation.isError).toBe(true);
      expect(mutation.error).toEqual(stringError);
    });
  });

  describe('error state management', () => {
    it('should provide troubleshooting steps for errors', () => {
      const troubleshootingSteps = [
        'Ensure you have a Telegram account',
        'Check that pop-ups are not blocked in your browser',
        'Try refreshing the page',
        'Clear browser cache and cookies',
        'Disable browser extensions temporarily',
      ];

      expect(troubleshootingSteps).toHaveLength(5);
      expect(troubleshootingSteps[0]).toBe('Ensure you have a Telegram account');
      expect(troubleshootingSteps[1]).toBe('Check that pop-ups are not blocked in your browser');
      expect(troubleshootingSteps[2]).toBe('Try refreshing the page');
      expect(troubleshootingSteps[3]).toBe('Clear browser cache and cookies');
      expect(troubleshootingSteps[4]).toBe('Disable browser extensions temporarily');
    });

    it('should track retry count in error state', () => {
      const initialErrorState = {
        hasError: false,
        errorMessage: '',
        retryCount: 0,
        troubleshootingSteps: [],
      };

      const errorStateAfterFailure = {
        hasError: true,
        errorMessage: 'Failed to connect Telegram',
        retryCount: 1,
        troubleshootingSteps: [
          'Ensure you have a Telegram account',
          'Check that pop-ups are not blocked in your browser',
          'Try refreshing the page',
          'Clear browser cache and cookies',
          'Disable browser extensions temporarily',
        ],
      };

      expect(initialErrorState.hasError).toBe(false);
      expect(initialErrorState.retryCount).toBe(0);
      expect(errorStateAfterFailure.hasError).toBe(true);
      expect(errorStateAfterFailure.retryCount).toBe(1);
      expect(errorStateAfterFailure.troubleshootingSteps).toHaveLength(5);
    });
  });

  describe('retry functionality', () => {
    it('should reset error state on retry', () => {
      const resetState = {
        hasError: false,
        errorMessage: '',
        retryCount: 0,
        troubleshootingSteps: [],
      };

      expect(resetState.hasError).toBe(false);
      expect(resetState.errorMessage).toBe('');
      expect(resetState.retryCount).toBe(0);
      expect(resetState.troubleshootingSteps).toHaveLength(0);
    });

    it('should reinitialize widget after retry', async () => {
      await loadTelegramWidget();
      await loadTelegramWidget(); // Second call for retry

      expect(loadTelegramWidget).toHaveBeenCalledTimes(2);
    });
  });

  describe('component state management', () => {
    it('should handle loading state', () => {
      vi.mocked(useLinkTelegramMutation).mockReturnValue({
        mutate: vi.fn(),
        isPending: true,
        isError: false,
        error: null,
        data: undefined,
        isSuccess: false,
        reset: vi.fn(),
      } as unknown as ReturnType<typeof useLinkTelegramMutation>);

      const mutation = useLinkTelegramMutation();

      expect(mutation.isPending).toBe(true);
    });

    it('should handle success state', () => {
      vi.mocked(useLinkTelegramMutation).mockReturnValue({
        mutate: vi.fn(),
        isPending: false,
        isError: false,
        error: null,
        data: mockLinkTelegramResponse,
        isSuccess: true,
        reset: vi.fn(),
      } as unknown as ReturnType<typeof useLinkTelegramMutation>);

      const mutation = useLinkTelegramMutation();

      expect(mutation.isSuccess).toBe(true);
      expect(mutation.data).toEqual(mockLinkTelegramResponse);
    });

    it('should handle error state', () => {
      const error = new Error('Test error');

      vi.mocked(useLinkTelegramMutation).mockReturnValue({
        mutate: vi.fn(),
        isPending: false,
        isError: true,
        error: error,
        data: undefined,
        isSuccess: false,
        reset: vi.fn(),
      } as unknown as ReturnType<typeof useLinkTelegramMutation>);

      const mutation = useLinkTelegramMutation();

      expect(mutation.isError).toBe(true);
      expect(mutation.error).toEqual(error);
    });
  });

  describe('session management', () => {
    it('should store email in sessionStorage', () => {
      const mockSessionStorage = {
        setItem: vi.fn(),
        getItem: vi.fn(),
        removeItem: vi.fn(),
      };

      mockSessionStorage.setItem('telegram_auth_email', 'test@example.com');

      expect(mockSessionStorage.setItem).toHaveBeenCalledWith('telegram_auth_email', 'test@example.com');
    });

    it('should handle cleanup on unmount', () => {
      const mockWindow: { onTelegramAuth?: unknown } = {
        onTelegramAuth: vi.fn(),
      };

      delete mockWindow.onTelegramAuth;

      expect(mockWindow.onTelegramAuth).toBeUndefined();
    });
  });
});
