import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import { vi } from 'vitest';
import { usePreferenceCategoriesQuery, useUserPreferencesQuery } from 'entities/preferences/api';
import { useAuthStore } from 'shared/store/auth';
import { UserPreferencesSection } from './UserPreferencesSection';

type MockedCategoriesQueryResult = ReturnType<typeof usePreferenceCategoriesQuery>;
type MockedUserPreferencesQueryResult = ReturnType<typeof useUserPreferencesQuery>;

vi.mock('shared/store/auth');
const mockUseAuthStore = vi.mocked(useAuthStore);

vi.mock('entities/preferences/api');
const mockUsePreferenceCategoriesQuery = vi.mocked(usePreferenceCategoriesQuery);
const mockUseUserPreferencesQuery = vi.mocked(useUserPreferencesQuery);

vi.mock('app/providers/ToastProvider', () => ({
  useToast: () => ({
    showInfo: vi.fn(),
    showSuccess: vi.fn(),
    showError: vi.fn(),
  }),
}));

const createWrapper = () => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });

  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  );
};

const mockCategoriesData = {
  categories: [
    {
      name: 'Travel',
      services: [
        {
          serviceType: 'Flight',
          preferenceTypes: [
            {
              serviceTypeName: 'Flight',
              name: 'Currency',
              dataType: 'String',
              isRequired: false,
            },
          ],
        },
      ],
    },
  ],
};

const mockUserPreferencesData = {
  preferences: [
    {
      preferenceTypeDisplayName: 'Flight.Currency',
      value: '"USD"',
    },
  ],
};

describe('UserPreferencesSection', () => {
  beforeEach(() => {
    vi.clearAllMocks();

    mockUsePreferenceCategoriesQuery.mockReturnValue({
      data: undefined,
      isLoading: false,
      error: null,
      refetch: vi.fn(),
    } as unknown as MockedCategoriesQueryResult);

    mockUseUserPreferencesQuery.mockReturnValue({
      data: undefined,
      isLoading: false,
      error: null,
      refetch: vi.fn(),
    } as unknown as MockedUserPreferencesQueryResult);
  });

  it('shows login message when user is not authenticated', () => {
    mockUseAuthStore.mockReturnValue({ user: null });

    render(<UserPreferencesSection />, { wrapper: createWrapper() });

    expect(screen.getByText('Please log in to manage your preferences.')).toBeInTheDocument();
  });

  it('shows loading state when preferences are loading', () => {
    mockUseAuthStore.mockReturnValue({ user: { username: 'testuser', email: 'test@example.com' } });

    mockUsePreferenceCategoriesQuery.mockReturnValue({
      data: undefined,
      isLoading: true,
      error: null,
      refetch: vi.fn(),
    } as unknown as MockedCategoriesQueryResult);

    render(<UserPreferencesSection />, { wrapper: createWrapper() });

    expect(screen.getByText('Loading your preferences...')).toBeInTheDocument();
  });

  it('shows error state when preferences fail to load', () => {
    mockUseAuthStore.mockReturnValue({ user: { username: 'testuser', email: 'test@example.com' } });

    mockUsePreferenceCategoriesQuery.mockReturnValue({
      data: undefined,
      isLoading: false,
      error: new Error('Failed to load'),
      refetch: vi.fn(),
    } as unknown as MockedCategoriesQueryResult);

    render(<UserPreferencesSection />, { wrapper: createWrapper() });

    expect(screen.getByText('Something Went Wrong')).toBeInTheDocument();
  });

  it('renders preferences form when queries are loaded', () => {
    mockUseAuthStore.mockReturnValue({ user: { username: 'testuser', email: 'test@example.com' } });

    mockUsePreferenceCategoriesQuery.mockReturnValue({
      data: mockCategoriesData,
      isLoading: false,
      error: null,
      refetch: vi.fn(),
    } as unknown as MockedCategoriesQueryResult);

    mockUseUserPreferencesQuery.mockReturnValue({
      data: mockUserPreferencesData,
      isLoading: false,
      error: null,
      refetch: vi.fn(),
    } as unknown as MockedUserPreferencesQueryResult);

    render(<UserPreferencesSection />, { wrapper: createWrapper() });

    expect(screen.getByText('Travel Preferences')).toBeInTheDocument();
    expect(screen.getByText('Travel')).toBeInTheDocument();
    expect(screen.getByText('Flight')).toBeInTheDocument();
  });
});
