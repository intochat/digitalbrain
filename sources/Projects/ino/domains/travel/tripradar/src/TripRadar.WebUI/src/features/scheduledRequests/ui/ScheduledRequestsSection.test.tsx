import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { useToast } from 'app/providers/ToastProvider';
import {
  useAirportSuggestionsQuery,
  useCreateScheduledRequestMutation,
  useDeleteScheduledExecutionMutation,
  useScheduledExecutionsQuery,
  useUpdateScheduledExecutionConfigurationMutation,
  useUpdateScheduledExecutionQueryMutation,
} from 'entities/scheduledRequests';
import { ScheduledRequestsSection } from './ScheduledRequestsSection';

vi.mock('entities/scheduledRequests');
vi.mock('app/providers/ToastProvider');

const mockUseToast = vi.mocked(useToast);
const mockUseAirportSuggestionsQuery = vi.mocked(useAirportSuggestionsQuery);
const mockUseScheduledExecutionsQuery = vi.mocked(useScheduledExecutionsQuery);
const mockUseCreateScheduledRequestMutation = vi.mocked(useCreateScheduledRequestMutation);
const mockUseUpdateScheduledExecutionConfigurationMutation = vi.mocked(
  useUpdateScheduledExecutionConfigurationMutation
);
const mockUseUpdateScheduledExecutionQueryMutation = vi.mocked(useUpdateScheduledExecutionQueryMutation);
const mockUseDeleteScheduledExecutionMutation = vi.mocked(useDeleteScheduledExecutionMutation);

describe('ScheduledRequestsSection', () => {
  beforeEach(() => {
    vi.clearAllMocks();

    mockUseToast.mockReturnValue({
      addToast: vi.fn(),
      removeToast: vi.fn(),
      toasts: [],
      showError: vi.fn(),
      showInfo: vi.fn(),
      showSuccess: vi.fn(),
    });

    mockUseAirportSuggestionsQuery.mockReturnValue({
      data: [],
      isFetching: false,
    } as unknown as ReturnType<typeof useAirportSuggestionsQuery>);

    mockUseScheduledExecutionsQuery.mockReturnValue({
      data: { scheduledExecutions: [] },
      isLoading: false,
      isError: false,
      isFetching: false,
      refetch: vi.fn(),
    } as unknown as ReturnType<typeof useScheduledExecutionsQuery>);

    mockUseCreateScheduledRequestMutation.mockReturnValue({
      mutateAsync: vi.fn(),
      isPending: false,
    } as unknown as ReturnType<typeof useCreateScheduledRequestMutation>);

    mockUseUpdateScheduledExecutionConfigurationMutation.mockReturnValue({
      mutateAsync: vi.fn(),
      isPending: false,
    } as unknown as ReturnType<typeof useUpdateScheduledExecutionConfigurationMutation>);

    mockUseUpdateScheduledExecutionQueryMutation.mockReturnValue({
      mutateAsync: vi.fn(),
      isPending: false,
    } as unknown as ReturnType<typeof useUpdateScheduledExecutionQueryMutation>);

    mockUseDeleteScheduledExecutionMutation.mockReturnValue({
      mutateAsync: vi.fn(),
      isPending: false,
    } as unknown as ReturnType<typeof useDeleteScheduledExecutionMutation>);
  });

  it('renders empty state when no scheduled executions exist', () => {
    render(<ScheduledRequestsSection />);

    expect(screen.getByText('Create New Request')).toBeInTheDocument();
    expect(screen.getByText('No scheduled requests yet')).toBeInTheDocument();
  });

  it('creates a flight scheduled request from form input', async () => {
    const mutateAsync = vi.fn().mockResolvedValue({ scheduledExecutionUniqueId: 'abc-123' });
    const refetch = vi.fn().mockResolvedValue(undefined);

    mockUseScheduledExecutionsQuery.mockReturnValue({
      data: { scheduledExecutions: [] },
      isLoading: false,
      isError: false,
      isFetching: false,
      refetch,
    } as unknown as ReturnType<typeof useScheduledExecutionsQuery>);

    mockUseCreateScheduledRequestMutation.mockReturnValue({
      mutateAsync,
      isPending: false,
    } as unknown as ReturnType<typeof useCreateScheduledRequestMutation>);

    render(<ScheduledRequestsSection />);

    fireEvent.change(screen.getByLabelText('Origin City or Airport'), { target: { value: 'JFK' } });
    fireEvent.change(screen.getByLabelText('Destination City or Airport'), { target: { value: 'LHR' } });
    fireEvent.change(screen.getByLabelText('Departure Date'), { target: { value: '2026-03-15' } });

    fireEvent.click(screen.getByRole('button', { name: 'Create Scheduled Request' }));

    await waitFor(() => {
      expect(mutateAsync).toHaveBeenCalledWith(
        expect.objectContaining({
          queryType: 'flights',
          payload: expect.objectContaining({
            departureAirportCode: 'JFK',
            destinationAirportCode: 'LHR',
            departureDate: '2026-03-15',
            schedule: expect.any(String),
            nextExecutionTime: expect.any(String),
          }),
        })
      );
    });

    await waitFor(() => {
      expect(refetch).toHaveBeenCalled();
    });
  });

  it('renders existing request cards and toggles active state', async () => {
    const updateMutation = vi.fn().mockResolvedValue(undefined);

    mockUseScheduledExecutionsQuery.mockReturnValue({
      data: {
        scheduledExecutions: [
          {
            scheduledExecutionUniqueId: 'request-1',
            serviceType: 'Flight',
            isActive: true,
            nextExecutionTime: '2026-03-10T12:00:00Z',
            schedule: '0 8 * * *',
            createdOn: '2026-02-01T08:00:00Z',
            requestSummary: 'Flight from JFK to LHR',
            departureAirportCity: 'new orleans',
            departureAirportCode: 'JFK',
            destinationAirportCity: 'london',
            destinationAirportCode: 'LHR',
          },
        ],
      },
      isLoading: false,
      isError: false,
      isFetching: false,
      refetch: vi.fn(),
    } as unknown as ReturnType<typeof useScheduledExecutionsQuery>);

    mockUseUpdateScheduledExecutionConfigurationMutation.mockReturnValue({
      mutateAsync: updateMutation,
      isPending: false,
    } as unknown as ReturnType<typeof useUpdateScheduledExecutionConfigurationMutation>);

    render(<ScheduledRequestsSection />);

    expect(screen.getByText('New Orleans (JFK) -> London (LHR)')).toBeInTheDocument();
    expect(screen.getByText('Route: New Orleans (JFK) to London (LHR)')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Pause' }));

    await waitFor(() => {
      expect(updateMutation).toHaveBeenCalledWith({
        uniqueId: 'request-1',
        configuration: {
          isActive: false,
          schedule: '0 8 * * *',
          nextExecutionTime: '2026-03-10T12:00:00Z',
        },
      });
    });
  });

  it('edits an existing flight request', async () => {
    const updateQueryMutation = vi.fn().mockResolvedValue(undefined);
    const updateConfigurationMutation = vi.fn().mockResolvedValue(undefined);

    mockUseScheduledExecutionsQuery.mockReturnValue({
      data: {
        scheduledExecutions: [
          {
            scheduledExecutionUniqueId: 'request-1',
            serviceType: 'Flight',
            isActive: true,
            nextExecutionTime: '2026-03-10T12:00:00Z',
            schedule: '0 8 * * *',
            createdOn: '2026-02-01T08:00:00Z',
            requestSummary: 'Flight from JFK to LHR',
            departureAirportCode: 'JFK',
            departureAirportCity: 'New York',
            destinationAirportCode: 'LHR',
            destinationAirportCity: 'London',
            departureDate: '2026-03-15T00:00:00Z',
          },
        ],
      },
      isLoading: false,
      isError: false,
      isFetching: false,
      refetch: vi.fn(),
    } as unknown as ReturnType<typeof useScheduledExecutionsQuery>);

    mockUseUpdateScheduledExecutionQueryMutation.mockReturnValue({
      mutateAsync: updateQueryMutation,
      isPending: false,
    } as unknown as ReturnType<typeof useUpdateScheduledExecutionQueryMutation>);

    mockUseUpdateScheduledExecutionConfigurationMutation.mockReturnValue({
      mutateAsync: updateConfigurationMutation,
      isPending: false,
    } as unknown as ReturnType<typeof useUpdateScheduledExecutionConfigurationMutation>);

    render(<ScheduledRequestsSection />);

    fireEvent.click(screen.getByRole('button', { name: 'Edit' }));
    fireEvent.change(screen.getByLabelText('Destination City or Airport'), { target: { value: 'CDG' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save Changes' }));

    await waitFor(() => {
      expect(updateQueryMutation).toHaveBeenCalledWith({
        uniqueId: 'request-1',
        request: expect.objectContaining({
          departureAirportCode: 'JFK',
          destinationAirportCode: 'CDG',
          departureDate: expect.any(String),
        }),
      });
    });

    await waitFor(() => {
      expect(updateConfigurationMutation).toHaveBeenCalledWith({
        uniqueId: 'request-1',
        configuration: expect.objectContaining({
          isActive: true,
          schedule: '0 8 * * *',
          nextExecutionTime: expect.any(String),
        }),
      });
    });
  });

  it('paginates scheduled request cards when there are many executions', async () => {
    mockUseScheduledExecutionsQuery.mockReturnValue({
      data: {
        scheduledExecutions: [
          {
            scheduledExecutionUniqueId: 'request-1',
            serviceType: 'Events',
            isActive: true,
            nextExecutionTime: '2026-03-10T12:00:00Z',
            schedule: '0 8 * * *',
            createdOn: '2026-02-01T08:00:00Z',
            requestSummary: 'Execution 1',
          },
          {
            scheduledExecutionUniqueId: 'request-2',
            serviceType: 'Events',
            isActive: true,
            nextExecutionTime: '2026-03-11T12:00:00Z',
            schedule: '0 8 * * *',
            createdOn: '2026-02-01T08:00:00Z',
            requestSummary: 'Execution 2',
          },
          {
            scheduledExecutionUniqueId: 'request-3',
            serviceType: 'Events',
            isActive: true,
            nextExecutionTime: '2026-03-12T12:00:00Z',
            schedule: '0 8 * * *',
            createdOn: '2026-02-01T08:00:00Z',
            requestSummary: 'Execution 3',
          },
          {
            scheduledExecutionUniqueId: 'request-4',
            serviceType: 'Events',
            isActive: true,
            nextExecutionTime: '2026-03-13T12:00:00Z',
            schedule: '0 8 * * *',
            createdOn: '2026-02-01T08:00:00Z',
            requestSummary: 'Execution 4',
          },
        ],
      },
      isLoading: false,
      isError: false,
      isFetching: false,
      refetch: vi.fn(),
    } as unknown as ReturnType<typeof useScheduledExecutionsQuery>);

    render(<ScheduledRequestsSection />);

    expect(screen.getByText('Page 1 of 2')).toBeInTheDocument();
    expect(screen.getByText('Execution 1')).toBeInTheDocument();
    expect(screen.getByText('Execution 2')).toBeInTheDocument();
    expect(screen.getByText('Execution 3')).toBeInTheDocument();
    expect(screen.queryByText('Execution 4')).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Next' }));

    await waitFor(() => {
      expect(screen.getByText('Page 2 of 2')).toBeInTheDocument();
      expect(screen.getByText('Execution 4')).toBeInTheDocument();
    });
  });

  it('shows paused scheduled executions in the list and counters', () => {
    mockUseScheduledExecutionsQuery.mockReturnValue({
      data: {
        scheduledExecutions: [
          {
            scheduledExecutionUniqueId: 'request-active',
            serviceType: 'Flight',
            isActive: true,
            nextExecutionTime: '2026-03-10T12:00:00Z',
            schedule: '0 8 * * *',
            createdOn: '2026-02-01T08:00:00Z',
            requestSummary: 'Visible execution',
          },
          {
            scheduledExecutionUniqueId: 'request-paused',
            serviceType: 'Flight',
            isActive: false,
            nextExecutionTime: '2026-03-09T12:00:00Z',
            schedule: '0 8 * * *',
            createdOn: '2026-02-01T08:00:00Z',
            requestSummary: 'Paused execution',
          },
        ],
      },
      isLoading: false,
      isError: false,
      isFetching: false,
      refetch: vi.fn(),
    } as unknown as ReturnType<typeof useScheduledExecutionsQuery>);

    render(<ScheduledRequestsSection />);

    expect(screen.getByText('Visible execution')).toBeInTheDocument();
    expect(screen.getByText('Paused execution')).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /All2/i })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /Flights2/i })).toBeInTheDocument();
  });
  it('renders error state when scheduled executions query fails', () => {
    mockUseScheduledExecutionsQuery.mockReturnValue({
      data: undefined,
      isLoading: false,
      isError: true,
      isFetching: false,
      refetch: vi.fn(),
    } as unknown as ReturnType<typeof useScheduledExecutionsQuery>);

    render(<ScheduledRequestsSection />);

    expect(screen.getByText('Unable to load scheduled requests')).toBeInTheDocument();
    expect(screen.queryByText('No scheduled requests yet')).not.toBeInTheDocument();
  });
});



