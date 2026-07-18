import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { useToast } from 'app/providers/ToastProvider';
import {
  useCreateTripVaultMutation,
  useDeleteTripVaultMutation,
  useTripVaultsQuery,
  useUpdateTripVaultMutation,
} from 'entities/tripVault';
import { TripVaultSection } from './TripVaultSection';

vi.mock('entities/tripVault');
vi.mock('app/providers/ToastProvider');

const mockUseToast = vi.mocked(useToast);
const mockUseTripVaultsQuery = vi.mocked(useTripVaultsQuery);
const mockUseCreateTripVaultMutation = vi.mocked(useCreateTripVaultMutation);
const mockUseUpdateTripVaultMutation = vi.mocked(useUpdateTripVaultMutation);
const mockUseDeleteTripVaultMutation = vi.mocked(useDeleteTripVaultMutation);
let showErrorMock: ReturnType<typeof vi.fn>;
let showSuccessMock: ReturnType<typeof vi.fn>;

const renderWithRouter = (component: JSX.Element) => {
  return render(<MemoryRouter>{component}</MemoryRouter>);
};

describe('TripVaultSection', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    showErrorMock = vi.fn();
    showSuccessMock = vi.fn();

    mockUseToast.mockReturnValue({
      addToast: vi.fn(),
      removeToast: vi.fn(),
      toasts: [],
      showError: showErrorMock,
      showInfo: vi.fn(),
      showSuccess: showSuccessMock,
    });

    mockUseTripVaultsQuery.mockReturnValue({
      data: [],
      isLoading: false,
      isError: false,
      isFetching: false,
      refetch: vi.fn(),
    } as unknown as ReturnType<typeof useTripVaultsQuery>);

    mockUseCreateTripVaultMutation.mockReturnValue({
      mutateAsync: vi.fn(),
      isPending: false,
    } as unknown as ReturnType<typeof useCreateTripVaultMutation>);

    mockUseUpdateTripVaultMutation.mockReturnValue({
      mutateAsync: vi.fn(),
      isPending: false,
    } as unknown as ReturnType<typeof useUpdateTripVaultMutation>);

    mockUseDeleteTripVaultMutation.mockReturnValue({
      mutateAsync: vi.fn(),
      isPending: false,
    } as unknown as ReturnType<typeof useDeleteTripVaultMutation>);
  });

  it('renders empty state when no trips exist', () => {
    renderWithRouter(<TripVaultSection />);

    expect(screen.getAllByText('Create Trip')).toHaveLength(2);
    expect(screen.getByText('No trips yet')).toBeInTheDocument();
    expect(screen.queryByText('Select a trip to view history')).not.toBeInTheDocument();
  });

  it('creates a trip vault from form input', async () => {
    const mutateAsync = vi.fn().mockResolvedValue({
      uniqueId: 'trip-1',
      name: 'Summer Europe',
      description: null,
      startDate: null,
      endDate: null,
      itemsCount: 0,
      createdOn: '2026-02-01T00:00:00Z',
    });

    mockUseCreateTripVaultMutation.mockReturnValue({
      mutateAsync,
      isPending: false,
    } as unknown as ReturnType<typeof useCreateTripVaultMutation>);

    renderWithRouter(<TripVaultSection />);

    fireEvent.change(screen.getByPlaceholderText('e.g. Spring in Lisbon'), { target: { value: 'Summer Europe' } });
    fireEvent.change(screen.getByPlaceholderText(/What is unique about this trip\?/i), {
      target: { value: 'Two weeks in Portugal' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Create Trip' }));

    await waitFor(() => {
      expect(mutateAsync).toHaveBeenCalledWith({
        name: 'Summer Europe',
        description: 'Two weeks in Portugal',
        startDate: null,
        endDate: null,
      });
    });
  });

  it('blocks creating duplicate trip names in a case-insensitive way', async () => {
    const mutateAsync = vi.fn().mockResolvedValue({
      uniqueId: 'trip-2',
      name: 'Another trip',
      description: null,
      startDate: null,
      endDate: null,
      itemsCount: 0,
      createdOn: '2026-02-01T00:00:00Z',
    });

    mockUseTripVaultsQuery.mockReturnValue({
      data: [
        {
          uniqueId: 'trip-1',
          name: 'Vienna',
          description: null,
          startDate: null,
          endDate: null,
          itemsCount: 0,
          createdOn: '2026-02-01T00:00:00Z',
        },
      ],
      isLoading: false,
      isError: false,
      isFetching: false,
      refetch: vi.fn(),
    } as unknown as ReturnType<typeof useTripVaultsQuery>);

    mockUseCreateTripVaultMutation.mockReturnValue({
      mutateAsync,
      isPending: false,
    } as unknown as ReturnType<typeof useCreateTripVaultMutation>);

    renderWithRouter(<TripVaultSection />);

    fireEvent.change(screen.getByPlaceholderText('e.g. Spring in Lisbon'), { target: { value: '  vienna  ' } });
    fireEvent.click(screen.getByRole('button', { name: 'Create Trip' }));

    await waitFor(() => {
      expect(showErrorMock).toHaveBeenCalledWith(
        'Validation error',
        'Trip name must be unique. A vault with this name already exists.'
      );
    });

    expect(mutateAsync).not.toHaveBeenCalled();
  });

  it('shows duplicate-name validation when API returns TRIP_VAULT_NAME_EXISTS', async () => {
    const mutateAsync = vi.fn().mockRejectedValue({ code: 'TRIP_VAULT_NAME_EXISTS' });

    mockUseCreateTripVaultMutation.mockReturnValue({
      mutateAsync,
      isPending: false,
    } as unknown as ReturnType<typeof useCreateTripVaultMutation>);

    renderWithRouter(<TripVaultSection />);

    fireEvent.change(screen.getByPlaceholderText('e.g. Spring in Lisbon'), { target: { value: 'Spring Lisbon' } });
    fireEvent.click(screen.getByRole('button', { name: 'Create Trip' }));

    await waitFor(() => {
      expect(showErrorMock).toHaveBeenCalledWith(
        'Validation error',
        'Trip name must be unique. A vault with this name already exists.'
      );
    });
  });

  it('applies non-past date limits for trip dates', () => {
    renderWithRouter(<TripVaultSection />);

    const startDateInput = screen.getByLabelText('Start Date') as HTMLInputElement;
    const endDateInput = screen.getByLabelText('End Date') as HTMLInputElement;
    const today = new Date().toISOString().slice(0, 10);
    const tomorrow = new Date(Date.now() + 24 * 60 * 60 * 1000).toISOString().slice(0, 10);

    expect(startDateInput.min).toBe(today);
    expect(endDateInput.min).toBe(today);

    fireEvent.change(startDateInput, { target: { value: tomorrow } });
    expect(endDateInput.min).toBe(tomorrow);
  });

  it('shows dedicated history action for each trip card', () => {
    mockUseTripVaultsQuery.mockReturnValue({
      data: [
        {
          uniqueId: 'trip-1',
          name: 'Autumn Paris',
          description: 'City trip',
          startDate: null,
          endDate: null,
          itemsCount: 1,
          createdOn: '2026-02-01T00:00:00Z',
        },
      ],
      isLoading: false,
      isError: false,
      isFetching: false,
      refetch: vi.fn(),
    } as unknown as ReturnType<typeof useTripVaultsQuery>);

    renderWithRouter(<TripVaultSection />);

    expect(screen.getByRole('button', { name: 'History' })).toBeInTheDocument();
  });

  it('shows trips pagination controls and navigates between pages', () => {
    mockUseTripVaultsQuery.mockReturnValue({
      data: [
        {
          uniqueId: 'trip-1',
          name: 'Trip 1',
          description: null,
          startDate: null,
          endDate: null,
          itemsCount: 0,
          createdOn: '2026-02-01T00:00:00Z',
        },
        {
          uniqueId: 'trip-2',
          name: 'Trip 2',
          description: null,
          startDate: null,
          endDate: null,
          itemsCount: 0,
          createdOn: '2026-02-02T00:00:00Z',
        },
        {
          uniqueId: 'trip-3',
          name: 'Trip 3',
          description: null,
          startDate: null,
          endDate: null,
          itemsCount: 0,
          createdOn: '2026-02-03T00:00:00Z',
        },
        {
          uniqueId: 'trip-4',
          name: 'Trip 4',
          description: null,
          startDate: null,
          endDate: null,
          itemsCount: 0,
          createdOn: '2026-02-04T00:00:00Z',
        },
        {
          uniqueId: 'trip-5',
          name: 'Trip 5',
          description: null,
          startDate: null,
          endDate: null,
          itemsCount: 0,
          createdOn: '2026-02-05T00:00:00Z',
        },
      ],
      isLoading: false,
      isError: false,
      isFetching: false,
      refetch: vi.fn(),
    } as unknown as ReturnType<typeof useTripVaultsQuery>);

    renderWithRouter(<TripVaultSection />);

    expect(screen.getByLabelText('Trips page size')).toBeInTheDocument();
    expect(screen.getAllByText('Page 1 of 2').length).toBeGreaterThan(0);
    expect(screen.queryByText('Trip 1')).not.toBeInTheDocument();

    const nextButtons = screen.getAllByRole('button', { name: 'Next' });
    fireEvent.click(nextButtons[0]);

    expect(screen.getAllByText('Page 2 of 2').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Trip 1').length).toBeGreaterThan(0);
  });
});
