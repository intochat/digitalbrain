import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import { BrowserRouter } from 'react-router-dom';
import { vi } from 'vitest';
import { useAuthStore } from 'shared/store/auth';
import { UserActions } from './UserActions';

// Mock the auth store
vi.mock('shared/store/auth');

const mockUseAuthStore = vi.mocked(useAuthStore);

const renderWithProviders = (component: React.ReactElement, initialPath = '/') => {
  window.history.pushState({}, '', initialPath);

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

describe('UserActions', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  describe('Unauthenticated state', () => {
    beforeEach(() => {
      mockUseAuthStore.mockReturnValue({
        isAuthenticated: false,
        user: null,
      });
    });

    it('should render sign-in button with design tokens', () => {
      renderWithProviders(<UserActions />);

      const loginButton = screen.getByRole('link', { name: /sign in to your account/i });

      expect(loginButton).toBeInTheDocument();

      // Check that design tokens are used instead of hardcoded colors
      expect(loginButton).toHaveClass('text-content-secondary', 'dark:text-content-secondary-dark');
      expect(loginButton).toHaveClass('hover:text-content', 'dark:hover:text-content-dark');
    });

    it('should have proper focus indicators for accessibility', () => {
      renderWithProviders(<UserActions />);

      const loginButton = screen.getByRole('link', { name: /sign in to your account/i });

      // Check focus classes
      expect(loginButton).toHaveClass('focus:outline-none', 'focus:text-content', 'dark:focus:text-content-dark');
    });

    it('should have minimum touch target size for mobile', () => {
      renderWithProviders(<UserActions />);

      const loginButton = screen.getByRole('link', { name: /sign in to your account/i });

      // Check minimum touch target classes and inline styles for 44px requirement
      expect(loginButton).toHaveClass('px-3', 'py-2', 'min-h-11', 'touch-manipulation');
      expect(loginButton).toHaveStyle({ minHeight: '44px' });
    });
  });

  describe('Authenticated state', () => {
    beforeEach(() => {
      mockUseAuthStore.mockReturnValue({
        isAuthenticated: true,
        user: {
          username: 'Sample_User42',
          name: 'Sample_User42',
          email: 'sample@example.com',
          avatar: 'https://example.com/avatar.jpg',
          subscription: 'free',
        },
      });
    });

    it('should render user profile link with design tokens', () => {
      renderWithProviders(<UserActions />);

      const profileLink = screen.getByRole('link');
      const avatar = screen.getByRole('img');
      const userName = screen.getByText('sample_user42');

      expect(profileLink).toBeInTheDocument();
      expect(avatar).toBeInTheDocument();
      expect(userName).toBeInTheDocument();

      // Check design token usage
      expect(profileLink).toHaveClass('hover:opacity-90');
      expect(profileLink).toHaveClass('focus:outline-none', 'focus:opacity-90');

      expect(avatar).toHaveClass('h-8', 'w-8', 'rounded-full', 'object-cover');
      expect(avatar).toHaveClass('transition-opacity', 'duration-200');

      expect(userName).toHaveClass('text-content', 'dark:text-content-dark');
    });

    it('should have proper touch target size for mobile', () => {
      renderWithProviders(<UserActions />);

      const profileLink = screen.getByRole('link');

      // Check that profile link has proper padding and minimum size for touch targets
      expect(profileLink).toHaveClass('p-2', 'min-h-11', 'min-w-11', 'touch-manipulation');
      expect(profileLink).toHaveStyle({ minHeight: '44px', minWidth: '44px' });
    });

    it('should hide username on small screens', () => {
      renderWithProviders(<UserActions />);

      const userName = screen.getByText('sample_user42');

      // Check responsive visibility classes
      expect(userName).toHaveClass('hidden', 'sm:block');
    });
  });
});
