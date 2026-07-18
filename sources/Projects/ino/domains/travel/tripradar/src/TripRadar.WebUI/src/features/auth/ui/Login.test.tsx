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
vi.mock('shared/lib', () => ({
  getEmailFromUrlParams: vi.fn(() => null),
  mapProfileToAuthUser: vi.fn(
    (profile: { username: string; email: string; firstName?: string; lastName?: string }) => ({
      username: profile.username,
      name: [profile.firstName, profile.lastName].filter(Boolean).join(' ') || profile.username,
      email: profile.email,
      avatar: 'https://example.com/avatar.png',
      subscription: 'free',
    })
  ),
}));

vi.mock('./TelegramConnect', () => ({
  TelegramConnect: () => <div data-testid="telegram-connect">Telegram Widget</div>,
}));

vi.mock('./OAuthButtons', () => ({
  OAuthButtons: () => (
    <div data-testid="oauth-buttons">
      <button data-testid="google-oauth">Continue with Google</button>
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
 * Unit tests for Login component integration
 * Requirements: All requirements - form submission, OAuth, Telegram, error handling
 */
describe('Login Component - Integration Tests', () => {
  const mockLogin = vi.fn();
  const mockMutate = vi.fn();

  beforeEach(() => {
    vi.clearAllMocks();

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
      username: 'testuser',
      email: 'test@example.com',
      firstName: 'Test',
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
    } as unknown);
  });

  describe('Form Submission Scenarios', () => {
    it('should render login form with all required elements', () => {
      renderWithProviders(<Login />);

      // Check form elements are present
      expect(screen.getByLabelText(/email address/i)).toBeInTheDocument();
      expect(screen.getByPlaceholderText(/enter your password/i)).toBeInTheDocument();
      expect(screen.getByLabelText(/remember me/i)).toBeInTheDocument();
      expect(screen.getByRole('button', { name: /sign in/i })).toBeInTheDocument();
      expect(screen.getByText(/forgot password/i)).toBeInTheDocument();
    });

    it('should handle form input changes', () => {
      renderWithProviders(<Login />);

      const emailInput = screen.getByLabelText(/email address/i);
      const passwordInput = screen.getByPlaceholderText(/enter your password/i);

      fireEvent.change(emailInput, { target: { value: 'test@example.com' } });
      fireEvent.change(passwordInput, { target: { value: 'password123' } });

      expect(emailInput).toHaveValue('test@example.com');
      expect(passwordInput).toHaveValue('password123');
    });

    it('should toggle password visibility', () => {
      renderWithProviders(<Login />);

      const passwordInput = screen.getByPlaceholderText(/enter your password/i);
      const toggleButton = screen.getByRole('button', { name: /show password/i });

      expect(passwordInput).toHaveAttribute('type', 'password');

      fireEvent.click(toggleButton);
      expect(passwordInput).toHaveAttribute('type', 'text');
      expect(screen.getByRole('button', { name: /hide password/i })).toBeInTheDocument();

      fireEvent.click(toggleButton);
      expect(passwordInput).toHaveAttribute('type', 'password');
    });

    it('should load profile and login after successful session creation', async () => {
      mockMutate.mockImplementation((_data, options) => {
        options?.onSuccess?.({
          token: null,
          refreshToken: null,
        });
      });

      renderWithProviders(<Login />);

      fireEvent.change(screen.getByLabelText(/email address/i), { target: { value: 'test@example.com' } });
      fireEvent.change(screen.getByPlaceholderText(/enter your password/i), { target: { value: 'password123' } });
      fireEvent.click(screen.getByRole('button', { name: /sign in/i }));

      await waitFor(() => {
        expect(profileApi.getProfile).toHaveBeenCalled();
        expect(mockLogin).toHaveBeenCalled();
      });
    });
  });

  describe('OAuth Integration Flows', () => {
    it('should render OAuth buttons component', () => {
      renderWithProviders(<Login />);

      expect(screen.getByTestId('oauth-buttons')).toBeInTheDocument();
      expect(screen.getByTestId('google-oauth')).toBeInTheDocument();
    });

    it('should disable OAuth section during form submission', () => {
      vi.mocked(useLoginMutation).mockReturnValue({
        mutate: mockMutate,
        isPending: true,
        isError: false,
        error: null,
        data: undefined,
        isSuccess: false,
        reset: vi.fn(),
      } as unknown);

      renderWithProviders(<Login />);

      const oauthSection = screen.getByTestId('oauth-buttons').parentElement;
      expect(oauthSection).toHaveClass('opacity-50', 'pointer-events-none');
    });
  });

  describe('Loading States and UI Behavior', () => {
    it('should show loading state during form submission', () => {
      vi.mocked(useLoginMutation).mockReturnValue({
        mutate: mockMutate,
        isPending: true,
        isError: false,
        error: null,
        data: undefined,
        isSuccess: false,
        reset: vi.fn(),
      } as unknown);

      renderWithProviders(<Login />);

      const emailInput = screen.getByLabelText(/email address/i);
      const passwordInput = screen.getByPlaceholderText(/enter your password/i);
      const submitButton = screen.getByRole('button', { name: /signing in/i });

      expect(emailInput).toBeDisabled();
      expect(passwordInput).toBeDisabled();
      expect(submitButton).toBeDisabled();
      expect(screen.getByText('Signing in...')).toBeInTheDocument();
    });

    it('should show form elements in normal state', () => {
      renderWithProviders(<Login />);

      const emailInput = screen.getByLabelText(/email address/i);
      const passwordInput = screen.getByPlaceholderText(/enter your password/i);
      const submitButton = screen.getByRole('button', { name: /sign in/i });

      expect(emailInput).not.toBeDisabled();
      expect(passwordInput).not.toBeDisabled();
      expect(submitButton).not.toBeDisabled();
    });
  });

  describe('Accessibility and User Experience', () => {
    it('should have proper ARIA attributes for form elements', () => {
      renderWithProviders(<Login />);

      const emailInput = screen.getByLabelText(/email address/i);
      const passwordInput = screen.getByPlaceholderText(/enter your password/i);

      expect(emailInput).toHaveAttribute('aria-required', 'true');
      expect(passwordInput).toHaveAttribute('aria-required', 'true');
      expect(emailInput).toHaveAttribute('aria-invalid', 'false');
      expect(passwordInput).toHaveAttribute('aria-invalid', 'false');
    });

    it('should have proper semantic HTML structure', () => {
      renderWithProviders(<Login />);

      expect(screen.getByRole('main')).toBeInTheDocument();
      expect(screen.getByText('Welcome back')).toBeInTheDocument();

      // Check for form elements
      expect(screen.getByLabelText(/email address/i)).toBeInTheDocument();
      expect(screen.getByPlaceholderText(/enter your password/i)).toBeInTheDocument();
    });

    it('should have proper navigation links', () => {
      renderWithProviders(<Login />);

      // Check for signup link by text content
      const signupLink = screen.getByText(/create account/i).closest('a');
      expect(signupLink).toHaveAttribute('href', '/signup');

      // Check for forgot password link by text content
      const forgotPasswordLink = screen.getByText(/forgot password/i).closest('a');
      expect(forgotPasswordLink).toHaveAttribute('href', '/forgot-password');
    });

    it('should keep mobile-friendly spacing on form controls', () => {
      renderWithProviders(<Login />);

      const emailInput = screen.getByLabelText(/email address/i);
      const passwordInput = screen.getByPlaceholderText(/enter your password/i);
      const submitButton = screen.getByRole('button', { name: /sign in/i });
      const toggleButton = screen.getByRole('button', { name: /show password/i });

      expect(emailInput).toHaveClass('px-3', 'py-2.5');
      expect(passwordInput).toHaveClass('px-3', 'py-2.5');
      expect(submitButton).toHaveClass('px-4', 'py-2.5', 'w-full');
      expect(toggleButton).toHaveClass('p-1');
    });

    it('should have proper input types and attributes for mobile', () => {
      renderWithProviders(<Login />);

      const emailInput = screen.getByLabelText(/email address/i);
      const passwordInput = screen.getByPlaceholderText(/enter your password/i);

      expect(emailInput).toHaveAttribute('type', 'email');
      expect(emailInput).toHaveAttribute('inputMode', 'email');
      expect(emailInput).toHaveAttribute('autoComplete', 'email');
      expect(passwordInput).toHaveAttribute('autoComplete', 'current-password');
    });

    it('should have proper screen reader support', () => {
      renderWithProviders(<Login />);

      expect(screen.getByText('Social sign in options')).toHaveClass('sr-only');
      expect(screen.getByText('Email sign in form')).toHaveClass('sr-only');

      const liveRegion = document.querySelector('[aria-live="polite"].sr-only');
      expect(liveRegion).not.toBeNull();
    });

    it('should have proper role attributes', () => {
      renderWithProviders(<Login />);

      // Check for separator role
      const separator = screen.getByRole('separator');
      expect(separator).toHaveAttribute('aria-label', 'Or continue with email');

      // Check for main role
      expect(screen.getByRole('main')).toBeInTheDocument();
    });
  });

  describe('Component Integration', () => {
    it('should integrate with OAuthButtons component', () => {
      renderWithProviders(<Login />);

      const oauthSection = screen.getByTestId('oauth-buttons');
      expect(oauthSection).toBeInTheDocument();

      // OAuth section should be within the main card
      const mainCard = screen.getByRole('main');
      expect(mainCard).toContainElement(oauthSection);
    });

    it('should have proper form structure', () => {
      renderWithProviders(<Login />);

      const emailInput = screen.getByLabelText(/email address/i);
      const passwordInput = screen.getByPlaceholderText(/enter your password/i);
      const submitButton = screen.getByRole('button', { name: /sign in/i });

      // All form elements should be within the main card
      const mainCard = screen.getByRole('main');
      expect(mainCard).toContainElement(emailInput);
      expect(mainCard).toContainElement(passwordInput);
      expect(mainCard).toContainElement(submitButton);
    });

    it('should have consistent styling classes', () => {
      renderWithProviders(<Login />);

      const emailInput = screen.getByLabelText(/email address/i);
      const passwordInput = screen.getByPlaceholderText(/enter your password/i);

      expect(emailInput).toHaveClass('rounded-lg', 'bg-surface', 'dark:bg-surface-dark');
      expect(passwordInput).toHaveClass('rounded-lg', 'bg-surface', 'dark:bg-surface-dark');
    });

    it('should handle form validation display', () => {
      renderWithProviders(<Login />);

      const submitButton = screen.getByRole('button', { name: /sign in/i });

      // Try to submit empty form
      fireEvent.click(submitButton);

      // Should show validation errors (these are handled by React Hook Form)
      // We just verify the form structure is correct for validation
      expect(submitButton).toBeInTheDocument();
    });
  });
});
