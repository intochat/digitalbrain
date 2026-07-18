import { render, screen, fireEvent, act } from '@testing-library/react';
import { vi } from 'vitest';
import { ToastNotification } from './ToastNotification';

describe('ToastNotification', () => {
  const mockOnClose = vi.fn();

  beforeEach(() => {
    vi.clearAllMocks();
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('renders success toast with title and message', () => {
    render(
      <ToastNotification
        id="test-1"
        type="success"
        title="Success!"
        message="Operation completed successfully"
        onClose={mockOnClose}
      />
    );

    expect(screen.getByText('Success!')).toBeInTheDocument();
    expect(screen.getByText('Operation completed successfully')).toBeInTheDocument();
    expect(screen.getByRole('alert')).toBeInTheDocument();
  });

  it('renders error toast with appropriate styling', () => {
    render(<ToastNotification id="test-2" type="error" title="Error occurred" onClose={mockOnClose} />);

    expect(screen.getByText('Error occurred')).toBeInTheDocument();
    const alertElement = screen.getByRole('alert');
    expect(alertElement).toHaveClass('border-red-200');
  });

  it('renders info toast without message', () => {
    render(<ToastNotification id="test-3" type="info" title="Information" onClose={mockOnClose} />);

    expect(screen.getByText('Information')).toBeInTheDocument();
    expect(screen.queryByText('Operation completed successfully')).not.toBeInTheDocument();
  });

  it('calls onClose when dismiss button is clicked', () => {
    render(<ToastNotification id="test-4" type="success" title="Test" onClose={mockOnClose} />);

    const dismissButton = screen.getByLabelText('Dismiss notification');
    fireEvent.click(dismissButton);

    expect(mockOnClose).toHaveBeenCalledWith('test-4');
  });

  it('auto-dismisses after specified duration', async () => {
    render(<ToastNotification id="test-5" type="success" title="Auto dismiss" duration={1000} onClose={mockOnClose} />);

    expect(mockOnClose).not.toHaveBeenCalled();

    act(() => {
      vi.advanceTimersByTime(1000);
    });

    expect(mockOnClose).toHaveBeenCalledWith('test-5');
  });

  it('does not auto-dismiss when duration is 0', () => {
    render(<ToastNotification id="test-6" type="success" title="No auto dismiss" duration={0} onClose={mockOnClose} />);

    vi.advanceTimersByTime(5000);

    expect(mockOnClose).not.toHaveBeenCalled();
  });
});
