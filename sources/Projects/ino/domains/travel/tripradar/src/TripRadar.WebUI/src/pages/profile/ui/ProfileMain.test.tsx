import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import { BrowserRouter } from 'react-router-dom';
import { ToastProvider } from 'app/providers/ToastProvider';
import { usePortalLanguagesQuery, usePortalTimezonesQuery } from 'entities/portal';
import { useProfileQuery, useUpdateProfileMutation } from 'entities/user/api';
import { useAuthStore } from 'shared/store/auth';
import { ProfileMain } from './ProfileMain';

// Mock the hooks
vi.mock('shared/store/auth');
vi.mock('entities/user/api');
vi.mock('entities/portal');

const mockUseAuthStore = vi.mocked(useAuthStore);
const mockUseProfileQuery = vi.mocked(useProfileQuery);
const mockUseUpdateProfileMutation = vi.mocked(useUpdateProfileMutation);
const mockUsePortalLanguagesQuery = vi.mocked(usePortalLanguagesQuery);
const mockUsePortalTimezonesQuery = vi.mocked(usePortalTimezonesQuery);

const createWrapper = () => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });

  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <ToastProvider>{children}</ToastProvider>
      </BrowserRouter>
    </QueryClientProvider>
  );
};

describe('ProfileMain', () => {
  beforeEach(() => {
    mockUseAuthStore.mockReturnValue({
      user: { username: 'testuser' },
      isAuthenticated: true,
      login: vi.fn(),
      logout: vi.fn(),
      setUser: vi.fn(),
    });

    mockUseUpdateProfileMutation.mockReturnValue({
      mutateAsync: vi.fn(),
      isPending: false,
      isError: false,
      error: null,
    } as unknown as ReturnType<typeof useUpdateProfileMutation>);

    mockUsePortalLanguagesQuery.mockReturnValue({
      data: {
        languages: [
          { languageCode: 'en', languageName: 'English' },
          { languageCode: 'ru', languageName: 'Russian' },
        ],
      },
      isLoading: false,
      error: null,
      isError: false,
      isSuccess: true,
      refetch: vi.fn(),
    } as unknown as ReturnType<typeof usePortalLanguagesQuery>);

    mockUsePortalTimezonesQuery.mockReturnValue({
      data: {
        timezones: [
          { timezoneId: 2, timezoneCode: 'America/New_York', timezoneName: 'Eastern Time (ET)' },
          { timezoneId: 10, timezoneCode: 'Europe/Berlin', timezoneName: 'Berlin (CET)' },
        ],
      },
      isLoading: false,
      error: null,
      isError: false,
      isSuccess: true,
      refetch: vi.fn(),
    } as unknown as ReturnType<typeof usePortalTimezonesQuery>);
  });

  it('renders loading state', () => {
    mockUseProfileQuery.mockReturnValue({
      data: undefined,
      isLoading: true,
      error: null,
      refetch: vi.fn(),
      isError: false,
      isSuccess: false,
    } as unknown as ReturnType<typeof useProfileQuery>);

    render(<ProfileMain />, { wrapper: createWrapper() });

    expect(screen.getByText('Loading profile...')).toBeInTheDocument();
  });

  it('renders error state', () => {
    mockUseProfileQuery.mockReturnValue({
      data: undefined,
      isLoading: false,
      error: new Error('Failed to load'),
      refetch: vi.fn(),
      isError: true,
      isSuccess: false,
    } as unknown as ReturnType<typeof useProfileQuery>);

    render(<ProfileMain />, { wrapper: createWrapper() });

    expect(screen.getByText('Failed to load profile')).toBeInTheDocument();
  });

  it('renders profile information when loaded', () => {
    const mockProfile = {
      username: 'testuser',
      email: 'test@example.com',
      isEmailConfirmed: true,
      firstName: 'Test',
      lastName: 'User',
      phoneNumber: '+1234567890',
      timezoneId: 1,
      tierName: 'basic',
      isActive: true,
      allowsMarketingEmails: false,
      createdOn: '2023-01-01T00:00:00Z',
    };

    mockUseProfileQuery.mockReturnValue({
      data: mockProfile,
      isLoading: false,
      error: null,
      refetch: vi.fn(),
      isError: false,
      isSuccess: true,
    } as unknown as ReturnType<typeof useProfileQuery>);

    render(<ProfileMain />, { wrapper: createWrapper() });

    expect(screen.getByText('Personal Information')).toBeInTheDocument();
    expect(screen.getByText('Manage your personal details and account information')).toBeInTheDocument();
    expect(screen.getByText('testuser')).toBeInTheDocument();
    expect(screen.getByText('test@example.com')).toBeInTheDocument();
  });
});
