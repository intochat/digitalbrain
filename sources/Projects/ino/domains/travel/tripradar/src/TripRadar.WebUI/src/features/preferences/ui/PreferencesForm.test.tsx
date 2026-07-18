import React from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, fireEvent, within } from '@testing-library/react';
import type { UserPreferences } from 'shared/api';
import { PreferencesForm } from './PreferencesForm';

const mockShowSuccess = vi.fn();
const mockShowError = vi.fn();

vi.mock('app/providers/ToastProvider', () => ({
  useToast: () => ({
    showSuccess: mockShowSuccess,
    showError: mockShowError,
  }),
}));

const mockMutateAsync = vi.fn();
vi.mock('entities/preferences/api', async () => {
  const actual = await vi.importActual('entities/preferences/api');
  return {
    ...actual,
    useUpdatePreferencesMutation: () => ({
      mutateAsync: mockMutateAsync,
    }),
  };
});

vi.mock('entities/payment/api', () => ({
  useSubscriptionQuery: () => ({
    data: { tierType: 'essential' },
    isLoading: false,
    isError: false,
  }),
}));

vi.mock('./usePreferenceOptions', () => ({
  usePreferenceOptions: () => ({
    currencyOptions: [
      { value: 'USD', label: 'USD (US Dollar)' },
      { value: 'EUR', label: 'EUR (Euro)' },
    ],
    languageOptions: [],
    isLoading: false,
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

describe('PreferencesForm', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockMutateAsync.mockResolvedValue(undefined);
  });

  const mockPreferences: UserPreferences = {
    Flight: { Currency: 'USD' },
    Hotel: { Currency: 'USD' },
  };

  it('renders with collapsible structure', () => {
    render(
      <PreferencesForm initialPreferences={mockPreferences} enabledPreferenceKeys={['Flight', 'Hotel', 'Maps']} />,
      {
        wrapper: createWrapper(),
      }
    );

    expect(screen.getByText('Travel')).toBeInTheDocument();
    expect(screen.getByText('Local Services')).toBeInTheDocument();
    expect(screen.getByText('Flights')).toBeInTheDocument();
    expect(screen.getByText('Hotels')).toBeInTheDocument();
    expect(screen.getByText('Maps')).toBeInTheDocument();
  });

  it('allows expanding and collapsing groups', () => {
    render(<PreferencesForm initialPreferences={mockPreferences} enabledPreferenceKeys={['Flight']} />, {
      wrapper: createWrapper(),
    });

    const flightHeader = screen.getByText('Flights');
    fireEvent.click(flightHeader);

    const flightButton = flightHeader.closest('button');
    expect(flightButton).toHaveAttribute('aria-expanded', 'true');
  });

  it('renders save button', () => {
    render(<PreferencesForm initialPreferences={mockPreferences} />, { wrapper: createWrapper() });

    expect(screen.getByText('Save Preferences')).toBeInTheDocument();
  });

  it('starts with all groups collapsed', () => {
    render(<PreferencesForm initialPreferences={mockPreferences} />, { wrapper: createWrapper() });

    const groupButtons = screen.getAllByRole('button').filter(button => button.hasAttribute('aria-expanded'));

    groupButtons.forEach(button => {
      expect(button).toHaveAttribute('aria-expanded', 'false');
    });
  });

  it('renders only services provided by the backend tree', () => {
    render(<PreferencesForm initialPreferences={mockPreferences} enabledPreferenceKeys={['Flight', 'Hotel']} />, {
      wrapper: createWrapper(),
    });

    expect(screen.getByText('Flights')).toBeInTheDocument();
    expect(screen.getByText('Hotels')).toBeInTheDocument();
    expect(screen.queryByText('Maps')).not.toBeInTheDocument();
  });

  it('auto-saves DeepSearch toggle to flight preferences immediately', async () => {
    render(<PreferencesForm initialPreferences={mockPreferences} />, { wrapper: createWrapper() });

    const deepSearchLabel = screen.getByText('Deep search');
    const deepSearchField = deepSearchLabel.closest('div')?.parentElement;
    expect(deepSearchField).toBeTruthy();
    const deepSearchSwitch = within(deepSearchField as HTMLElement).getByRole('switch');

    fireEvent.click(deepSearchSwitch);

    await vi.waitFor(() => {
      expect(mockMutateAsync).toHaveBeenCalledTimes(1);
      expect(mockMutateAsync).toHaveBeenCalledWith({
        preferences: expect.objectContaining({
          Flight: expect.objectContaining({
            DeepSearch: true,
          }),
        }),
      });
    });
  });

  it('does not render no-trace toggle in travel preferences', () => {
    render(<PreferencesForm initialPreferences={mockPreferences} />, { wrapper: createWrapper() });

    expect(screen.queryByText('No-trace mode (all services)')).not.toBeInTheDocument();
  });
});
