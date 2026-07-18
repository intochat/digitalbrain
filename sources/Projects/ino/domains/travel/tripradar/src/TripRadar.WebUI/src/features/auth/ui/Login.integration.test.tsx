import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { BrowserRouter } from 'react-router-dom';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { useLoginMutation } from 'entities/auth';
import { profileApi } from 'entities/user/api';
import { useAuthStore } from 'shared/store/auth';
import { Login } from './Login';

// Mock dependencies
vi.mock('entities/auth');
vi.mock('entities/user/api', () => ({
  profileApi: {
    getProfile: vi.fn(),
  },
}));
vi.mock('shared/store/auth');
vi.mock('./TelegramConnect', () => ({
  TelegramConnect: ({
    email,
    mode = 'activation',
    onSuccess,
    onError,
    onAuthenticated,
  }: {
    email?: string;
    mode?: 'activation' | 'usernameSync' | 'signIn';
    onSuccess: (response: unknown) => void;
    onError: (error: string) => void;
    onAuthenticated?: () => void | Promise<void>;
  }) => (
    <div data-testid="telegram-connect">
      <div>Mode: {mode}</div>
      {email && <div>Email: {email}</div>}
      <button
        onClick={() => {
          if (mode === 'signIn') {
            void onAuthenticated?.();
            return;
          }

          onSuccess({
            token: 'test-token',
            refreshToken: 'test-refresh-token',
            email,
            username: 'testuser',
            message: 'Success',
          });
        }}
        data-testid="telegram-success"
      >
        Success
      </button>
      <button onClick={() => onError('Test error')} data-testid="telegram-error">
        Error
      </button>
    </div>
  ),
}));

const renderWithProviders = (component: React.ReactElement) => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>{component}</BrowserRouter>
    </QueryClientProvider>
  );
};

/**
 * Integration tests for Login component Telegram functionality
 * Requirements: Maintain existing Telegram functionality
 */
describe('Login Component - Telegram Integration', () => {
  const mockLogin = vi.fn();
  const mockMutate = vi.fn();

  beforeEach(() => {
    vi.clearAllMocks();

    // Mock useAuthStore as a function that returns different values based on the selector
    vi.mocked(useAuthStore).mockImplementation((selector: (state: unknown) => unknown) => {
      const state = {
        login: mockLogin,
        logout: vi.fn(),
        user: null,
        isAuthenticated: false,
        isLoading: false,
      };
      return selector ? selector(state) : state;
    });

    vi.mocked(profileApi.getProfile).mockResolvedValue({
      username: 'telegram-user',
      email: 'test@example.com',
      firstName: 'Telegram',
      lastName: 'User',
      isEmailConfirmed: true,
      timezoneId: 1,
      profilePictureUrl: null,
      timezoneName: 'UTC',
      languageName: 'English',
      countryName: 'United States',
      allowsMarketingEmails: true,
      isActive: true,
      createdOn: '2026-01-01T00:00:00Z',
      tierName: 'basic',
    });

    vi.mocked(useLoginMutation).mockReturnValue({
      mutate: mockMutate,
      isPending: false,
      isError: false,
      error: null,
      data: undefined,
      isSuccess: false,
      reset: vi.fn(),
    });
  });

  it('should show Telegram widget when TELEGRAM_REQUIRED error occurs', async () => {
    // Mock the login mutation to return TELEGRAM_REQUIRED error BEFORE rendering
    mockMutate.mockImplementation((data, options) => {
      if (options?.onError) {
        setTimeout(() => {
          options.onError({
            isTelegramRequired: true,
            email: 'test@example.com',
            name: 'LoginError',
            message: 'Telegram required',
          } as Error);
        }, 0);
      }
    });

    renderWithProviders(<Login />);

    // Fill in email and password
    const emailInput = screen.getByLabelText(/email address/i);
    const passwordInput = screen.getByPlaceholderText(/enter your password/i);
    const submitButton = screen.getByRole('button', { name: /sign in/i });

    fireEvent.change(emailInput, { target: { value: 'test@example.com' } });
    fireEvent.change(passwordInput, { target: { value: 'password123' } });

    fireEvent.click(submitButton);

    await waitFor(
      () => {
        expect(screen.getByTestId('telegram-connect')).toBeInTheDocument();
        expect(screen.getByText('Email: test@example.com')).toBeInTheDocument();
      },
      { timeout: 3000 }
    );
  });

  it('should fallback to submitted email when TELEGRAM_REQUIRED error has no email', async () => {
    mockMutate.mockImplementation((data, options) => {
      if (options?.onError) {
        setTimeout(() => {
          options.onError({
            isTelegramRequired: true,
            name: 'LoginError',
            message: 'Telegram required',
          } as Error);
        }, 0);
      }
    });

    renderWithProviders(<Login />);

    const emailInput = screen.getByLabelText(/email address/i);
    const passwordInput = screen.getByPlaceholderText(/enter your password/i);
    const submitButton = screen.getByRole('button', { name: /sign in/i });

    fireEvent.change(emailInput, { target: { value: 'fallback@example.com' } });
    fireEvent.change(passwordInput, { target: { value: 'password123' } });

    fireEvent.click(submitButton);

    await waitFor(
      () => {
        expect(screen.getByTestId('telegram-connect')).toBeInTheDocument();
        expect(screen.getByText('Email: fallback@example.com')).toBeInTheDocument();
      },
      { timeout: 3000 }
    );
  });

  it('should handle Telegram success and redirect', async () => {
    // Mock the login mutation to return TELEGRAM_REQUIRED error BEFORE rendering
    mockMutate.mockImplementation((data, options) => {
      if (options?.onError) {
        setTimeout(() => {
          options.onError({
            isTelegramRequired: true,
            email: 'test@example.com',
            name: 'LoginError',
            message: 'Telegram required',
          } as Error);
        }, 0);
      }
    });

    renderWithProviders(<Login />);

    // Trigger Telegram widget display
    const emailInput = screen.getByLabelText(/email address/i);
    const passwordInput = screen.getByPlaceholderText(/enter your password/i);
    const submitButton = screen.getByRole('button', { name: /sign in/i });

    fireEvent.change(emailInput, { target: { value: 'test@example.com' } });
    fireEvent.change(passwordInput, { target: { value: 'password123' } });

    fireEvent.click(submitButton);

    await waitFor(
      () => {
        expect(screen.getByTestId('telegram-connect')).toBeInTheDocument();
      },
      { timeout: 3000 }
    );

    // Simulate Telegram success
    const telegramSuccessButton = screen.getByTestId('telegram-success');
    fireEvent.click(telegramSuccessButton);

    await waitFor(() => {
      expect(mockLogin).toHaveBeenCalledWith({
        username: 'testuser',
        name: 'testuser',
        email: 'test@example.com',
        avatar: expect.stringContaining('ui-avatars.com'),
        subscription: 'free',
      });
    });
  });

  it('should sign in through Telegram from OAuth buttons', async () => {
    renderWithProviders(<Login />);

    const telegramButton = screen.getByRole('button', { name: /continue with telegram/i });
    fireEvent.click(telegramButton);

    await waitFor(() => {
      expect(screen.getByTestId('telegram-connect')).toBeInTheDocument();
      expect(screen.getByText('Mode: signIn')).toBeInTheDocument();
    });

    const telegramSuccessButton = screen.getByTestId('telegram-success');
    fireEvent.click(telegramSuccessButton);

    await waitFor(() => {
      expect(mockLogin).toHaveBeenCalledWith({
        username: 'telegram-user',
        name: 'Telegram User',
        email: 'test@example.com',
        avatar: expect.stringContaining('ui-avatars.com'),
        subscription: 'free',
      });
    });
  });

  it('should display Telegram error using ErrorAlert component', async () => {
    // Mock the login mutation to return TELEGRAM_REQUIRED error BEFORE rendering
    mockMutate.mockImplementation((data, options) => {
      if (options?.onError) {
        setTimeout(() => {
          options.onError({
            isTelegramRequired: true,
            email: 'test@example.com',
            name: 'LoginError',
            message: 'Telegram required',
          } as Error);
        }, 0);
      }
    });

    renderWithProviders(<Login />);

    // Trigger Telegram widget display
    const emailInput = screen.getByLabelText(/email address/i);
    const passwordInput = screen.getByPlaceholderText(/enter your password/i);
    const submitButton = screen.getByRole('button', { name: /sign in/i });

    fireEvent.change(emailInput, { target: { value: 'test@example.com' } });
    fireEvent.change(passwordInput, { target: { value: 'password123' } });

    fireEvent.click(submitButton);

    await waitFor(
      () => {
        expect(screen.getByTestId('telegram-connect')).toBeInTheDocument();
      },
      { timeout: 3000 }
    );

    // Simulate Telegram error
    const telegramErrorButton = screen.getByTestId('telegram-error');
    fireEvent.click(telegramErrorButton);

    await waitFor(() => {
      expect(screen.getByText('Telegram Connection Failed')).toBeInTheDocument();
      expect(screen.getByText('Test error')).toBeInTheDocument();
      expect(screen.getByRole('button', { name: /try logging in again/i })).toBeInTheDocument();
    });
  });

  it('should hide Telegram widget when error is dismissed', async () => {
    // Mock the login mutation to return TELEGRAM_REQUIRED error BEFORE rendering
    mockMutate.mockImplementation((data, options) => {
      if (options?.onError) {
        setTimeout(() => {
          options.onError({
            isTelegramRequired: true,
            email: 'test@example.com',
            name: 'LoginError',
            message: 'Telegram required',
          } as unknown);
        }, 0);
      }
    });

    renderWithProviders(<Login />);

    // Trigger Telegram widget display and error
    const emailInput = screen.getByLabelText(/email address/i);
    const passwordInput = screen.getByPlaceholderText(/enter your password/i);
    const submitButton = screen.getByRole('button', { name: /sign in/i });

    fireEvent.change(emailInput, { target: { value: 'test@example.com' } });
    fireEvent.change(passwordInput, { target: { value: 'password123' } });

    fireEvent.click(submitButton);

    await waitFor(
      () => {
        expect(screen.getByTestId('telegram-connect')).toBeInTheDocument();
      },
      { timeout: 3000 }
    );

    // Simulate Telegram error
    const telegramErrorButton = screen.getByTestId('telegram-error');
    fireEvent.click(telegramErrorButton);

    await waitFor(() => {
      expect(screen.getByText('Telegram Connection Failed')).toBeInTheDocument();
    });

    // Dismiss the error
    const dismissButton = screen.getByRole('button', { name: /try logging in again/i });
    fireEvent.click(dismissButton);

    await waitFor(() => {
      expect(screen.queryByTestId('telegram-connect')).not.toBeInTheDocument();
      expect(screen.queryByText('Telegram Connection Failed')).not.toBeInTheDocument();
    });
  });
});
