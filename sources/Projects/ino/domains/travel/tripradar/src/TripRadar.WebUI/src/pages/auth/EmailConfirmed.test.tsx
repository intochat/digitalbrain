import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { BrowserRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ThemeProvider } from 'app/providers/ThemeContext';
import { EmailConfirmed } from './EmailConfirmed';

// Mock the TelegramConnect component to test different scenarios
const mockTelegramConnect = vi.fn();

interface TelegramResponse {
  username?: string;
  email: string;
  token: string;
  refreshToken: string;
}

vi.mock('features/auth/ui/TelegramConnect', () => ({
  TelegramConnect: (props: {
    email: string;
    onSuccess: (response: TelegramResponse) => void;
    onError: (error: string) => void;
  }) => {
    mockTelegramConnect(props);
    return (
      <div data-testid="telegram-connect">
        <div>TelegramConnect with email: {props.email}</div>
        <button
          data-testid="telegram-success-btn"
          onClick={() =>
            props.onSuccess({
              username: 'testuser',
              email: props.email,
              token: 'test-token',
              refreshToken: 'test-refresh',
            })
          }
        >
          Simulate Success
        </button>
        <button data-testid="telegram-error-btn" onClick={() => props.onError('Telegram connection failed')}>
          Simulate Error
        </button>
      </div>
    );
  },
}));

// Mock the auth store
const mockLogin = vi.fn();
vi.mock('shared/store/auth', () => ({
  useAuthStore: () => ({
    login: mockLogin,
  }),
}));

// Mock react-router-dom navigate
const mockNavigate = vi.fn();
vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual('react-router-dom');
  return {
    ...actual,
    useNavigate: () => mockNavigate,
  };
});

const renderWithProviders = (component: React.ReactElement, searchParams = '') => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <ThemeProvider>
        <BrowserRouter>
          {(() => {
            window.history.replaceState({}, '', `${window.location.pathname || '/'}${searchParams}`);
            return component;
          })()}
        </BrowserRouter>
      </ThemeProvider>
    </QueryClientProvider>
  );
};

// Mock window.matchMedia for ThemeProvider
Object.defineProperty(window, 'matchMedia', {
  writable: true,
  value: vi.fn().mockImplementation(query => ({
    matches: false,
    media: query,
    onchange: null,
    addListener: vi.fn(), // deprecated
    removeListener: vi.fn(), // deprecated
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
    dispatchEvent: vi.fn(),
  })),
});

describe('EmailConfirmed Session Management', () => {
  beforeEach(() => {
    // Clear sessionStorage before each test
    sessionStorage.clear();
    // Clear URL search params
    window.location.search = '';
    // Clear mocks
    vi.clearAllMocks();
  });

  it('should retrieve email from URL parameters when available', () => {
    renderWithProviders(<EmailConfirmed />, '?email=test@example.com');

    // Should show TelegramConnect with the email from URL
    expect(screen.getByTestId('telegram-connect')).toHaveTextContent('TelegramConnect with email: test@example.com');
  });

  it('should fallback to sessionStorage when URL parameter is not available', () => {
    // Set email in sessionStorage
    sessionStorage.setItem('registration_email', 'session@example.com');

    renderWithProviders(<EmailConfirmed />);

    // Should show TelegramConnect with the email from sessionStorage
    expect(screen.getByTestId('telegram-connect')).toHaveTextContent('TelegramConnect with email: session@example.com');
  });

  it('should show error message when email is not available in URL or sessionStorage', () => {
    // No email in URL or sessionStorage
    renderWithProviders(<EmailConfirmed />);

    // Should show error message
    expect(screen.getByText('No registration data found')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Return to login page to sign in to your account' })).toBeInTheDocument();
  });

  it('should prioritize URL parameter over sessionStorage', () => {
    // Set both URL parameter and sessionStorage
    sessionStorage.setItem('registration_email', 'session@example.com');
    renderWithProviders(<EmailConfirmed />, '?email=url@example.com');

    // Should use URL parameter, not sessionStorage
    expect(screen.getByTestId('telegram-connect')).toHaveTextContent('TelegramConnect with email: url@example.com');
  });
});

describe('EmailConfirmed Responsive Design', () => {
  beforeEach(() => {
    sessionStorage.clear();
    window.location.search = '';
  });

  it('should apply responsive classes for touch-friendly interactions', () => {
    renderWithProviders(<EmailConfirmed />);

    // Check that the return to login button has touch-friendly sizing
    const loginButton = screen.getByRole('button', { name: 'Return to login page to sign in to your account' });
    expect(loginButton).toHaveClass('min-h-[44px]'); // Minimum touch target size
    expect(loginButton).toHaveClass('touch-manipulation'); // Optimized for touch
    expect(loginButton).toHaveClass('py-3'); // Adequate padding for mobile
  });

  it('should have responsive spacing and sizing classes', () => {
    // Set email to show the main content
    sessionStorage.setItem('registration_email', 'test@example.com');

    const { container } = renderWithProviders(<EmailConfirmed />);

    // Check main container has responsive padding - find the outermost div
    const mainContainer = container.querySelector('div[class*="px-4"]');
    expect(mainContainer).toHaveClass('px-4');
    expect(mainContainer).toHaveClass('sm:px-6');
    expect(mainContainer).toHaveClass('md:px-8');
    expect(mainContainer).toHaveClass('lg:px-12');
  });

  it('should have responsive text sizing', () => {
    sessionStorage.setItem('registration_email', 'test@example.com');

    renderWithProviders(<EmailConfirmed />);

    // Check title has responsive text sizing
    const title = screen.getByText('Email Confirmed');
    expect(title).toHaveClass('text-xl');
    expect(title).toHaveClass('sm:text-2xl');
    expect(title).toHaveClass('md:text-3xl');
    expect(title).toHaveClass('lg:text-4xl');
  });
});

describe('EmailConfirmed Telegram Integration', () => {
  beforeEach(() => {
    sessionStorage.clear();
    window.location.search = '';
    vi.clearAllMocks();
  });

  it('should handle successful Telegram connection and redirect to profile', async () => {
    sessionStorage.setItem('registration_email', 'test@example.com');

    renderWithProviders(<EmailConfirmed />);

    // Verify TelegramConnect is rendered with correct email
    expect(mockTelegramConnect).toHaveBeenCalledWith(
      expect.objectContaining({
        email: 'test@example.com',
        onSuccess: expect.any(Function),
        onError: expect.any(Function),
      })
    );

    // Simulate successful Telegram connection
    const successButton = screen.getByTestId('telegram-success-btn');
    fireEvent.click(successButton);

    // Verify login was called with correct user data
    await waitFor(() => {
      expect(mockLogin).toHaveBeenCalledWith({
        username: 'testuser',
        name: 'testuser',
        email: 'test@example.com',
        avatar: expect.stringContaining('ui-avatars.com'),
        subscription: 'free',
      });
    });

    // Verify navigation to profile
    await waitFor(() => {
      expect(mockNavigate).toHaveBeenCalledWith('/profile', { replace: true });
    });
  });

  it('should handle Telegram connection errors and display error message', async () => {
    sessionStorage.setItem('registration_email', 'test@example.com');

    renderWithProviders(<EmailConfirmed />);

    // Simulate Telegram connection error
    const errorButton = screen.getByTestId('telegram-error-btn');
    fireEvent.click(errorButton);

    // Verify error message is displayed
    await waitFor(() => {
      expect(screen.getByText('Telegram Connection Failed')).toBeInTheDocument();
      expect(screen.getByText('Telegram connection failed')).toBeInTheDocument();
    });

    // Verify error alert has retry functionality
    const tryAgainButton = screen.getByRole('button', { name: 'Try again' });
    expect(tryAgainButton).toBeInTheDocument();

    // Test retry functionality
    fireEvent.click(tryAgainButton);
    await waitFor(() => {
      expect(screen.queryByText('Telegram Connection Failed')).not.toBeInTheDocument();
    });
  });

  it('should handle missing username in Telegram response', async () => {
    sessionStorage.setItem('registration_email', 'test@example.com');

    renderWithProviders(<EmailConfirmed />);

    // Get the TelegramConnect props from the mock call
    const telegramProps = mockTelegramConnect.mock.calls[0][0];

    // Simulate Telegram response without username
    telegramProps.onSuccess({ email: 'test@example.com', token: 'test-token', refreshToken: 'test-refresh' });

    // Verify error message is displayed
    await waitFor(() => {
      expect(screen.getByText('Telegram Connection Failed')).toBeInTheDocument();
      expect(screen.getByText('Username not received from server. Please try again.')).toBeInTheDocument();
    });

    // Verify login was not called
    expect(mockLogin).not.toHaveBeenCalled();
    expect(mockNavigate).not.toHaveBeenCalled();
  });
});

describe('EmailConfirmed Navigation and Redirect', () => {
  beforeEach(() => {
    sessionStorage.clear();
    window.location.search = '';
    vi.clearAllMocks();
  });

  it('should redirect to login when Return to Login button is clicked', () => {
    renderWithProviders(<EmailConfirmed />);

    const loginButton = screen.getByRole('button', { name: 'Return to login page to sign in to your account' });
    fireEvent.click(loginButton);

    expect(mockNavigate).toHaveBeenCalledWith('/signin');
  });

  it('should handle URL parameter changes correctly', () => {
    // Start with no email
    const initialRender = renderWithProviders(<EmailConfirmed />);
    expect(screen.getByText('No registration data found')).toBeInTheDocument();
    initialRender.unmount();

    // Re-render with updated URL
    renderWithProviders(<EmailConfirmed />, '?email=new@example.com');
    expect(mockTelegramConnect).toHaveBeenCalledWith(
      expect.objectContaining({
        email: 'new@example.com',
      })
    );
  });
});

describe('EmailConfirmed Theme Support', () => {
  beforeEach(() => {
    sessionStorage.clear();
    window.location.search = '';
    vi.clearAllMocks();
    // Clear any existing theme classes
    document.documentElement.classList.remove('dark');
  });

  it('should use design tokens for all colors and styling', () => {
    sessionStorage.setItem('registration_email', 'test@example.com');

    const { container } = renderWithProviders(<EmailConfirmed />);

    // Check main background uses design tokens (gradient removed per design system)
    const backgroundDiv = container.querySelector('.bg-surface');
    expect(backgroundDiv).toBeInTheDocument();

    // Check card uses design tokens
    const card = container.querySelector('main');
    expect(card).toHaveClass('bg-surface');
    expect(card).toHaveClass('dark:bg-surface-dark');
    expect(card).toHaveClass('border-outline');
    expect(card).toHaveClass('dark:border-outline-dark');

    // Check text uses design tokens
    const title = screen.getByText('Email Confirmed');
    expect(title).toHaveClass('text-content');
    expect(title).toHaveClass('dark:text-content-dark');

    const description = screen.getByText('Connect your Telegram to complete registration');
    expect(description).toHaveClass('text-content-secondary');
    expect(description).toHaveClass('dark:text-content-secondary-dark');

    // Check icon uses design tokens
    const icon = container.querySelector('.text-green-600');
    expect(icon).toHaveClass('text-green-600');
    expect(icon).toHaveClass('dark:text-green-400');
  });

  it('should work correctly in dark mode', () => {
    // Simulate dark mode
    document.documentElement.classList.add('dark');
    sessionStorage.setItem('registration_email', 'test@example.com');

    const { container } = renderWithProviders(<EmailConfirmed />);

    // Verify dark mode classes are present and functional
    const card = container.querySelector('main');
    expect(card).toHaveClass('dark:bg-surface-dark');

    const title = screen.getByText('Email Confirmed');
    expect(title).toHaveClass('dark:text-content-dark');
  });

  it('should have proper hover states with design tokens', () => {
    renderWithProviders(<EmailConfirmed />);

    const loginButton = screen.getByRole('button', { name: 'Return to login page to sign in to your account' });

    // Check button uses design tokens for all states
    expect(loginButton).toHaveClass('bg-button');
    expect(loginButton).toHaveClass('dark:bg-button-dark');
    expect(loginButton).toHaveClass('text-button-text');
    expect(loginButton).toHaveClass('dark:text-button-text-dark');
    expect(loginButton).toHaveClass('hover:bg-button-hover');
    expect(loginButton).toHaveClass('dark:hover:bg-button-hover-dark');
  });

  it('should maintain consistent styling across theme changes', () => {
    sessionStorage.setItem('registration_email', 'test@example.com');

    // Test light mode
    const { container, rerender } = renderWithProviders(<EmailConfirmed />);

    const lightModeCard = container.querySelector('.bg-surface');
    expect(lightModeCard).toHaveClass('bg-surface');
    expect(lightModeCard).toHaveClass('dark:bg-surface-dark');

    // Switch to dark mode
    document.documentElement.classList.add('dark');

    // Re-render in dark mode
    rerender(
      <QueryClientProvider
        client={new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })}
      >
        <ThemeProvider>
          <BrowserRouter>
            <EmailConfirmed />
          </BrowserRouter>
        </ThemeProvider>
      </QueryClientProvider>
    );

    // Verify same design token classes are still present
    const darkModeCard = container.querySelector('.bg-surface');
    expect(darkModeCard).toHaveClass('bg-surface');
    expect(darkModeCard).toHaveClass('dark:bg-surface-dark');
  });
});

describe('EmailConfirmed Error Handling', () => {
  beforeEach(() => {
    sessionStorage.clear();
    window.location.search = '';
    vi.clearAllMocks();
  });

  it('should handle malformed URL parameters gracefully', () => {
    renderWithProviders(<EmailConfirmed />, '?email=invalid-email-format');

    // Should still render TelegramConnect even with malformed email
    expect(mockTelegramConnect).toHaveBeenCalledWith(
      expect.objectContaining({
        email: 'invalid-email-format',
      })
    );
  });

  it('should handle empty sessionStorage values', () => {
    sessionStorage.setItem('registration_email', '');

    renderWithProviders(<EmailConfirmed />);

    // Should show error state for empty email
    expect(screen.getByText('No registration data found')).toBeInTheDocument();
  });

  it('should handle console warnings for missing email data', () => {
    const consoleSpy = vi.spyOn(console, 'warn').mockImplementation(() => {});

    renderWithProviders(<EmailConfirmed />);

    expect(consoleSpy).toHaveBeenCalledWith('⚠️ No email found in URL or sessionStorage');

    consoleSpy.mockRestore();
  });

  it('should handle error alert dismissal', async () => {
    sessionStorage.setItem('registration_email', 'test@example.com');

    renderWithProviders(<EmailConfirmed />);

    // Trigger error
    const errorButton = screen.getByTestId('telegram-error-btn');
    fireEvent.click(errorButton);

    // Verify error is shown
    await waitFor(() => {
      expect(screen.getByText('Telegram Connection Failed')).toBeInTheDocument();
    });

    // Find and click dismiss button (X button)
    const dismissButton = screen.getByLabelText('Dismiss alert');
    fireEvent.click(dismissButton);

    // Verify error is dismissed
    await waitFor(() => {
      expect(screen.queryByText('Telegram Connection Failed')).not.toBeInTheDocument();
    });
  });
});
