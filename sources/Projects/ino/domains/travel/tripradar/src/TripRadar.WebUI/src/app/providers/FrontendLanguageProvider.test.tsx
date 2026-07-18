import { render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { useProfileQuery } from 'entities/user/api';
import { useAuthStore } from 'shared/store/auth';
import { FrontendLanguageProvider } from './FrontendLanguageProvider';

const { mockFrontendI18n } = vi.hoisted(() => ({
  mockFrontendI18n: {
    resolvedLanguage: 'en',
    language: 'en',
    changeLanguage: vi.fn().mockResolvedValue(undefined),
    t: (key: string) => key,
  },
}));

vi.mock('app/i18n', () => ({
  frontendI18n: mockFrontendI18n,
}));

vi.mock('entities/user/api', () => ({
  useProfileQuery: vi.fn(),
}));

const mockUseProfileQuery = vi.mocked(useProfileQuery);

describe('FrontendLanguageProvider', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFrontendI18n.resolvedLanguage = 'en';
    mockFrontendI18n.language = 'en';
    useAuthStore.setState({
      isAuthenticated: false,
      isLoading: false,
      user: null,
    });
  });

  it('renders children immediately for guest users', () => {
    mockUseProfileQuery.mockReturnValue({
      data: undefined,
      isLoading: false,
      isFetching: false,
      isError: false,
    } as unknown as ReturnType<typeof useProfileQuery>);

    render(
      <FrontendLanguageProvider>
        <div data-testid="language-provider-content">content</div>
      </FrontendLanguageProvider>
    );

    expect(mockUseProfileQuery).toHaveBeenCalledWith({ enabled: false });
    expect(screen.getByTestId('language-provider-content')).toBeInTheDocument();
    expect(mockFrontendI18n.changeLanguage).not.toHaveBeenCalled();
  });

  it('renders loading state while authenticated profile is loading', () => {
    useAuthStore.setState({ isAuthenticated: true });
    mockUseProfileQuery.mockReturnValue({
      data: undefined,
      isLoading: true,
      isFetching: true,
      isError: false,
    } as unknown as ReturnType<typeof useProfileQuery>);

    render(
      <FrontendLanguageProvider>
        <div data-testid="language-provider-content">content</div>
      </FrontendLanguageProvider>
    );

    expect(screen.getByText('Loading...')).toBeInTheDocument();
    expect(screen.queryByTestId('language-provider-content')).not.toBeInTheDocument();
  });

  it('applies language from profile for authenticated users', async () => {
    useAuthStore.setState({ isAuthenticated: true });
    mockUseProfileQuery.mockReturnValue({
      data: { languageCode: 'ru' },
      isLoading: false,
      isFetching: false,
      isError: false,
    } as unknown as ReturnType<typeof useProfileQuery>);

    render(
      <FrontendLanguageProvider>
        <div data-testid="language-provider-content">content</div>
      </FrontendLanguageProvider>
    );

    await waitFor(() => {
      expect(mockFrontendI18n.changeLanguage).toHaveBeenCalledWith('ru');
    });
    expect(screen.getByTestId('language-provider-content')).toBeInTheDocument();
  });

  it('falls back to current frontend language when profile query fails', () => {
    useAuthStore.setState({ isAuthenticated: true });
    mockFrontendI18n.resolvedLanguage = 'ru';
    mockFrontendI18n.language = 'ru';
    mockUseProfileQuery.mockReturnValue({
      data: undefined,
      isLoading: false,
      isFetching: false,
      isError: true,
    } as unknown as ReturnType<typeof useProfileQuery>);

    render(
      <FrontendLanguageProvider>
        <div data-testid="language-provider-content">content</div>
      </FrontendLanguageProvider>
    );

    expect(screen.getByTestId('language-provider-content')).toBeInTheDocument();
    expect(mockFrontendI18n.changeLanguage).not.toHaveBeenCalled();
  });
});
