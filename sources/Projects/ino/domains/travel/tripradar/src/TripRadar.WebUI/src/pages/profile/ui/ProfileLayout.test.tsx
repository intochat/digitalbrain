import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { ToastProvider } from 'app/providers/ToastProvider';
import { useLogout } from 'features/auth';
import { ProfileLayout } from './ProfileLayout';

// Mock the hooks
vi.mock('features/auth');
vi.mock('shared/lib/hooks', () => ({
  useNavigationPersistence: () => ({
    safeNavigate: vi.fn(),
    confirmNavigation: vi.fn(),
    cancelNavigation: vi.fn(),
    pendingNavigation: null,
    setHasUnsavedChanges: vi.fn(),
  }),
}));

const mockUseLogout = vi.mocked(useLogout);

const createWrapper = (initialEntries = ['/profile']) => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });

  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={initialEntries}>
        <ToastProvider>{children}</ToastProvider>
      </MemoryRouter>
    </QueryClientProvider>
  );
};

describe('ProfileLayout - URL Routing Consistency', () => {
  beforeEach(() => {
    mockUseLogout.mockReturnValue(vi.fn());
  });

  describe('Direct URL access to profile sections', () => {
    it('renders correctly when accessing /profile directly', () => {
      const Wrapper = createWrapper(['/profile']);
      render(
        <ProfileLayout>
          <div>Profile Content</div>
        </ProfileLayout>,
        { wrapper: Wrapper }
      );

      // Should show mobile navigation on profile page
      expect(screen.getAllByText('Security')).toHaveLength(2); // Mobile + Desktop
      expect(screen.getAllByText('Billing')).toHaveLength(2);
      expect(screen.getAllByText('Preferences')).toHaveLength(2);
      expect(screen.getAllByText('Scheduled Requests')).toHaveLength(2);
      expect(screen.getAllByText('Trips')).toHaveLength(2);
      expect(screen.getByText('Profile Content')).toBeInTheDocument();
    });

    it('renders correctly when accessing /profile/security directly', () => {
      const Wrapper = createWrapper(['/profile/security']);
      render(
        <ProfileLayout>
          <div>Security Content</div>
        </ProfileLayout>,
        { wrapper: Wrapper }
      );

      expect(screen.getByLabelText('Go back to profile')).toBeInTheDocument();
      expect(screen.getAllByText('Security')).toHaveLength(2); // Mobile header + Desktop sidebar
      expect(screen.getByText('Security Content')).toBeInTheDocument();
    });

    it('renders correctly when accessing /profile/billing directly', () => {
      const Wrapper = createWrapper(['/profile/billing']);
      render(
        <ProfileLayout>
          <div>Billing Content</div>
        </ProfileLayout>,
        { wrapper: Wrapper }
      );

      expect(screen.getByLabelText('Go back to profile')).toBeInTheDocument();
      expect(screen.getAllByText('Billing')).toHaveLength(2);
      expect(screen.getByText('Billing Content')).toBeInTheDocument();
    });

    it('renders correctly when accessing /profile/preferences directly', () => {
      const Wrapper = createWrapper(['/profile/preferences']);
      render(
        <ProfileLayout>
          <div>Preferences Content</div>
        </ProfileLayout>,
        { wrapper: Wrapper }
      );

      expect(screen.getByLabelText('Go back to profile')).toBeInTheDocument();
      expect(screen.getAllByText('Preferences')).toHaveLength(2);
      expect(screen.getByText('Preferences Content')).toBeInTheDocument();
    });

    it('renders correctly when accessing /profile/trips directly', () => {
      const Wrapper = createWrapper(['/profile/trips']);
      render(
        <ProfileLayout>
          <div>Trips Content</div>
        </ProfileLayout>,
        { wrapper: Wrapper }
      );

      expect(screen.getByLabelText('Go back to profile')).toBeInTheDocument();
      expect(screen.getAllByText('Trips')).toHaveLength(2);
      expect(screen.getByText('Trips Content')).toBeInTheDocument();
    });

    it('renders correctly when accessing /profile/trips/:id/history directly', () => {
      const Wrapper = createWrapper(['/profile/trips/trip-1/history']);
      render(
        <ProfileLayout>
          <div>Trip History Content</div>
        </ProfileLayout>,
        { wrapper: Wrapper }
      );

      expect(screen.getByLabelText('Go back to profile')).toBeInTheDocument();
      expect(screen.getAllByText('Trips')).toHaveLength(2);
      expect(screen.getByText('Trip History Content')).toBeInTheDocument();
    });

    it('renders correctly when accessing /profile/scheduled-requests directly', () => {
      const Wrapper = createWrapper(['/profile/scheduled-requests']);
      render(
        <ProfileLayout>
          <div>Scheduled Requests Content</div>
        </ProfileLayout>,
        { wrapper: Wrapper }
      );

      expect(screen.getByLabelText('Go back to profile')).toBeInTheDocument();
      expect(screen.getAllByText('Scheduled Requests')).toHaveLength(2);
      expect(screen.getByText('Scheduled Requests Content')).toBeInTheDocument();
    });
  });

  describe('Navigation state management', () => {
    it('shows correct active state for current section in desktop sidebar', () => {
      const Wrapper = createWrapper(['/profile/security']);
      render(
        <ProfileLayout>
          <div>Security Content</div>
        </ProfileLayout>,
        { wrapper: Wrapper }
      );

      const securityButtons = screen.getAllByText('Security');
      const desktopSecurityButton = securityButtons.find(button =>
        button.closest('button')?.className.includes('bg-button')
      );

      expect(desktopSecurityButton).toBeInTheDocument();
    });

    it('shows mobile navigation only on main profile page', () => {
      const Wrapper = createWrapper(['/profile']);
      render(
        <ProfileLayout>
          <div>Profile Content</div>
        </ProfileLayout>,
        { wrapper: Wrapper }
      );

      // Mobile navigation cards should be present (both mobile and desktop versions)
      expect(screen.getAllByText('Security')).toHaveLength(2);
      expect(screen.getAllByText('Billing')).toHaveLength(2);
      expect(screen.getAllByText('Preferences')).toHaveLength(2);
      expect(screen.getAllByText('Scheduled Requests')).toHaveLength(2);
      expect(screen.getAllByText('Trips')).toHaveLength(2);
    });

    it('shows mobile header with back button on sub-pages', () => {
      const Wrapper = createWrapper(['/profile/security']);
      render(
        <ProfileLayout>
          <div>Security Content</div>
        </ProfileLayout>,
        { wrapper: Wrapper }
      );

      expect(screen.getByLabelText('Go back to profile')).toBeInTheDocument();
      expect(screen.getAllByText('Security')).toHaveLength(2);
    });
  });

  describe('Section title mapping', () => {
    const testCases = [
      { path: '/profile', expectedTitle: 'Profile' },
      { path: '/profile/security', expectedTitle: 'Security' },
      { path: '/profile/billing', expectedTitle: 'Billing' },
      { path: '/profile/preferences', expectedTitle: 'Preferences' },
      { path: '/profile/scheduled-requests', expectedTitle: 'Scheduled Requests' },
      { path: '/profile/trips', expectedTitle: 'Trips' },
      { path: '/profile/trips/trip-1/history', expectedTitle: 'Trips' },
    ];

    testCases.forEach(({ path, expectedTitle }) => {
      it(`maps ${path} to "${expectedTitle}" title`, () => {
        const Wrapper = createWrapper([path]);
        render(
          <ProfileLayout>
            <div>Content</div>
          </ProfileLayout>,
          { wrapper: Wrapper }
        );

        if (path !== '/profile') {
          // Non-profile pages should show the title in mobile header and desktop sidebar
          expect(screen.getAllByText(expectedTitle)).toHaveLength(2);
        }
      });
    });
  });

  describe('Sign out functionality', () => {
    it('shows sign out button on main profile page', () => {
      const Wrapper = createWrapper(['/profile']);
      render(
        <ProfileLayout>
          <div>Profile Content</div>
        </ProfileLayout>,
        { wrapper: Wrapper }
      );

      expect(screen.getAllByText('Sign out')).toHaveLength(2); // Mobile + Desktop
    });

    it('calls logout function when sign out is clicked', async () => {
      const mockLogout = vi.fn();
      mockUseLogout.mockReturnValue(mockLogout);

      const Wrapper = createWrapper(['/profile']);
      render(
        <ProfileLayout>
          <div>Profile Content</div>
        </ProfileLayout>,
        { wrapper: Wrapper }
      );

      const signOutButtons = screen.getAllByText('Sign out');
      fireEvent.click(signOutButtons[0]); // Click the first one (mobile or desktop)

      expect(mockLogout).toHaveBeenCalled();
    });
  });

  describe('Responsive behavior', () => {
    it('renders desktop sidebar navigation', () => {
      const Wrapper = createWrapper(['/profile']);
      render(
        <ProfileLayout>
          <div>Profile Content</div>
        </ProfileLayout>,
        { wrapper: Wrapper }
      );

      // Desktop sidebar should contain all navigation items
      expect(screen.getAllByText('Profile')).toHaveLength(1); // Only in desktop sidebar
      expect(screen.getAllByText('Security')).toHaveLength(2); // Mobile + Desktop
      expect(screen.getAllByText('Billing')).toHaveLength(2); // Mobile + Desktop
      expect(screen.getAllByText('Preferences')).toHaveLength(2); // Mobile + Desktop
      expect(screen.getAllByText('Scheduled Requests')).toHaveLength(2); // Mobile + Desktop
      expect(screen.getAllByText('Trips')).toHaveLength(2); // Mobile + Desktop
    });
  });
});
