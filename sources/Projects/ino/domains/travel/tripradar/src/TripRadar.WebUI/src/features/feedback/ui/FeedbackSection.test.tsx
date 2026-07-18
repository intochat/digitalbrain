import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { useToast } from 'app/providers/ToastProvider';
import { useCreateFeedbackMutation, useFeedbackCategoriesQuery } from 'entities/feedback';
import { useAuthStore } from 'shared/store/auth';
import { FeedbackSection } from './FeedbackSection';

vi.mock('entities/feedback');
vi.mock('app/providers/ToastProvider');
vi.mock('shared/store/auth');

const mockUseToast = vi.mocked(useToast);
const mockUseFeedbackCategoriesQuery = vi.mocked(useFeedbackCategoriesQuery);
const mockUseCreateFeedbackMutation = vi.mocked(useCreateFeedbackMutation);
const mockUseAuthStore = vi.mocked(useAuthStore);

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

const createCategory = (name: string) => ({ name });

describe('FeedbackSection', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseAuthStore.mockReturnValue({ user: null } as ReturnType<typeof useAuthStore>);

    mockUseToast.mockReturnValue({
      addToast: vi.fn(),
      removeToast: vi.fn(),
      toasts: [],
      showError: vi.fn(),
      showInfo: vi.fn(),
      showSuccess: vi.fn(),
    });

    mockUseFeedbackCategoriesQuery.mockReturnValue({
      data: [createCategory('General'), createCategory('BugReport'), createCategory('FeatureRequest')],
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof useFeedbackCategoriesQuery>);

    mockUseCreateFeedbackMutation.mockReturnValue({
      mutateAsync: vi.fn(),
      isPending: false,
    } as unknown as ReturnType<typeof useCreateFeedbackMutation>);
  });

  it('shows sign-in prompt when user is not authenticated', () => {
    render(<FeedbackSection />, { wrapper: createWrapper() });

    expect(screen.getByText('Sign in to submit feedback with your Telegram username.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Send feedback' })).not.toBeInTheDocument();
  });

  it('shows feedback form when user is authenticated', () => {
    mockUseAuthStore.mockReturnValue({
      user: { username: 'test-user' },
    } as ReturnType<typeof useAuthStore>);

    render(<FeedbackSection />, { wrapper: createWrapper() });

    expect(screen.getByText('Category')).toBeInTheDocument();
    expect(screen.getByText('Details')).toBeInTheDocument();
  });

  it('submits feedback with category and rating', async () => {
    mockUseAuthStore.mockReturnValue({
      user: { username: 'test-user' },
    } as ReturnType<typeof useAuthStore>);

    const mutateAsync = vi.fn().mockResolvedValue({});

    mockUseCreateFeedbackMutation.mockReturnValue({
      mutateAsync,
      isPending: false,
    } as unknown as ReturnType<typeof useCreateFeedbackMutation>);

    render(<FeedbackSection />, { wrapper: createWrapper() });

    const textarea = screen.getByPlaceholderText('Describe your experience, issue, or idea.');
    fireEvent.change(textarea, {
      target: { value: 'The search form freezes every time when selecting return date.' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Rate 4 stars' }));
    fireEvent.click(screen.getByRole('button', { name: 'Send feedback' }));

    await waitFor(() => {
      expect(mutateAsync).toHaveBeenCalledWith(
        expect.objectContaining({
          content: 'The search form freezes every time when selecting return date.',
          feedbackCategoryType: 'general',
          rating: 4,
        })
      );
    });
  });

  it('shows validation error when content is too short', async () => {
    mockUseAuthStore.mockReturnValue({
      user: { username: 'test-user' },
    } as ReturnType<typeof useAuthStore>);

    render(<FeedbackSection />, { wrapper: createWrapper() });

    const textarea = screen.getByPlaceholderText('Describe your experience, issue, or idea.');
    fireEvent.change(textarea, { target: { value: 'Short' } });
    fireEvent.click(screen.getByRole('button', { name: 'Send feedback' }));

    await waitFor(() => {
      expect(screen.getByText('Feedback must contain at least 10 characters.')).toBeInTheDocument();
    });
  });
});
