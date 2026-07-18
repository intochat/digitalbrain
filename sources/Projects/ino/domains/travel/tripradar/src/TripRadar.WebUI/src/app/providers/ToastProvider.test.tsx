import { render, screen, fireEvent, act } from '@testing-library/react';
import { vi } from 'vitest';
import { useToast } from './hooks';
import { ToastProvider } from './ToastProvider';

// Test component to use the toast hook
const TestComponent = () => {
  const { showSuccess, showError, showInfo, toasts } = useToast();

  return (
    <div>
      <button onClick={() => showSuccess('Success', 'Success message')}>Show Success</button>
      <button onClick={() => showError('Error', 'Error message')}>Show Error</button>
      <button onClick={() => showInfo('Info')}>Show Info</button>
      <div data-testid="toast-count">{toasts.length}</div>
    </div>
  );
};

describe('ToastProvider', () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('provides toast context to children', () => {
    render(
      <ToastProvider>
        <TestComponent />
      </ToastProvider>
    );

    expect(screen.getByText('Show Success')).toBeInTheDocument();
    expect(screen.getByTestId('toast-count')).toHaveTextContent('0');
  });

  it('shows success toast when showSuccess is called', () => {
    render(
      <ToastProvider>
        <TestComponent />
      </ToastProvider>
    );

    fireEvent.click(screen.getByText('Show Success'));

    expect(screen.getByText('Success')).toBeInTheDocument();
    expect(screen.getByText('Success message')).toBeInTheDocument();
    expect(screen.getByTestId('toast-count')).toHaveTextContent('1');
  });

  it('shows error toast when showError is called', () => {
    render(
      <ToastProvider>
        <TestComponent />
      </ToastProvider>
    );

    fireEvent.click(screen.getByText('Show Error'));

    expect(screen.getByText('Error')).toBeInTheDocument();
    expect(screen.getByText('Error message')).toBeInTheDocument();
  });

  it('shows info toast without message', () => {
    render(
      <ToastProvider>
        <TestComponent />
      </ToastProvider>
    );

    fireEvent.click(screen.getByText('Show Info'));

    expect(screen.getByText('Info')).toBeInTheDocument();
    expect(screen.queryByText('Info message')).not.toBeInTheDocument();
  });

  it('removes toast when dismissed', () => {
    render(
      <ToastProvider>
        <TestComponent />
      </ToastProvider>
    );

    fireEvent.click(screen.getByText('Show Success'));
    expect(screen.getByTestId('toast-count')).toHaveTextContent('1');

    const dismissButton = screen.getByLabelText('Dismiss notification');
    fireEvent.click(dismissButton);

    expect(screen.getByTestId('toast-count')).toHaveTextContent('0');
  });

  it('auto-removes toast after duration', () => {
    render(
      <ToastProvider>
        <TestComponent />
      </ToastProvider>
    );

    fireEvent.click(screen.getByText('Show Success'));
    expect(screen.getByTestId('toast-count')).toHaveTextContent('1');

    act(() => {
      vi.advanceTimersByTime(5000);
    });

    expect(screen.getByTestId('toast-count')).toHaveTextContent('0');
  });

  it('handles multiple toasts', () => {
    render(
      <ToastProvider>
        <TestComponent />
      </ToastProvider>
    );

    fireEvent.click(screen.getByText('Show Success'));
    fireEvent.click(screen.getByText('Show Error'));
    fireEvent.click(screen.getByText('Show Info'));

    expect(screen.getByTestId('toast-count')).toHaveTextContent('3');
  });
});
