import { fireEvent, render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { ErrorConfig } from '../lib/errorMessages';
import { ErrorAlert } from './ErrorAlert';

/**
 * Unit tests for ErrorAlert component
 * Requirements: 7.1, 7.2, 7.3, 7.5
 */
describe('ErrorAlert Component', () => {
  const mockOnDismiss = vi.fn();
  const mockActionClick = vi.fn();

  beforeEach(() => {
    vi.clearAllMocks();
  });

  describe('rendering with different severity levels', () => {
    it('should render error severity with correct styling and icon', () => {
      const errorConfig: ErrorConfig = {
        title: 'Error Title',
        message: 'Error message',
        severity: 'error',
      };

      render(<ErrorAlert {...errorConfig} />);

      expect(screen.getByText('Error Title')).toBeInTheDocument();
      expect(screen.getByText('Error message')).toBeInTheDocument();

      // Check for error-specific styling classes
      const container = screen.getByRole('alert');
      expect(container).toHaveClass(
        'bg-surface-accent',
        'dark:bg-surface-accent-dark',
        'border-outline',
        'dark:border-outline-dark'
      );

      // Check for error icon (FaExclamationCircle)
      const icon = container.querySelector('svg');
      expect(icon).toBeInTheDocument();
      expect(icon).toHaveClass('text-red-600', 'dark:text-red-400');
    });

    it('should render warning severity with correct styling and icon', () => {
      const warningConfig: ErrorConfig = {
        title: 'Warning Title',
        message: 'Warning message',
        severity: 'warning',
      };

      render(<ErrorAlert {...warningConfig} />);

      expect(screen.getByText('Warning Title')).toBeInTheDocument();
      expect(screen.getByText('Warning message')).toBeInTheDocument();

      // Check for warning-specific styling classes
      const container = screen.getByRole('alert');
      expect(container).toHaveClass(
        'bg-surface-accent',
        'dark:bg-surface-accent-dark',
        'border-outline',
        'dark:border-outline-dark'
      );

      // Check for warning icon color
      const icon = container.querySelector('svg');
      expect(icon).toBeInTheDocument();
      expect(icon).toHaveClass('text-yellow-600', 'dark:text-yellow-400');
    });

    it('should render info severity with correct styling and icon', () => {
      const infoConfig: ErrorConfig = {
        title: 'Info Title',
        message: 'Info message',
        severity: 'info',
      };

      render(<ErrorAlert {...infoConfig} />);

      expect(screen.getByText('Info Title')).toBeInTheDocument();
      expect(screen.getByText('Info message')).toBeInTheDocument();

      // Check for info-specific styling classes
      const container = screen.getByRole('alert');
      expect(container).toHaveClass(
        'bg-surface-accent',
        'dark:bg-surface-accent-dark',
        'border-outline',
        'dark:border-outline-dark'
      );

      // Check for info icon color
      const icon = container.querySelector('svg');
      expect(icon).toBeInTheDocument();
      expect(icon).toHaveClass('text-blue-600', 'dark:text-blue-400');
    });
  });

  describe('action button rendering and onClick behavior', () => {
    it('should render action buttons with correct labels and variants', () => {
      const configWithActions: ErrorConfig = {
        title: 'Error with Actions',
        message: 'Please choose an action',
        severity: 'error',
        actions: [
          {
            label: 'Primary Action',
            onClick: mockActionClick,
            variant: 'primary',
          },
          {
            label: 'Secondary Action',
            onClick: mockActionClick,
            variant: 'secondary',
          },
        ],
      };

      render(<ErrorAlert {...configWithActions} />);

      const primaryButton = screen.getByRole('button', { name: 'Primary Action' });
      const secondaryButton = screen.getByRole('button', { name: 'Secondary Action' });

      expect(primaryButton).toBeInTheDocument();
      expect(secondaryButton).toBeInTheDocument();

      // Check primary button styling
      expect(primaryButton).toHaveClass('bg-primary-600', 'hover:bg-primary-700', 'text-white');

      // Check secondary button styling
      expect(secondaryButton).toHaveClass('border', 'border-outline', 'dark:border-outline-dark');
    });

    it('should call onClick handlers when action buttons are clicked', () => {
      const primaryAction = vi.fn();
      const secondaryAction = vi.fn();

      const configWithActions: ErrorConfig = {
        title: 'Error with Actions',
        message: 'Please choose an action',
        severity: 'error',
        actions: [
          {
            label: 'Primary Action',
            onClick: primaryAction,
            variant: 'primary',
          },
          {
            label: 'Secondary Action',
            onClick: secondaryAction,
            variant: 'secondary',
          },
        ],
      };

      render(<ErrorAlert {...configWithActions} />);

      const primaryButton = screen.getByRole('button', { name: 'Primary Action' });
      const secondaryButton = screen.getByRole('button', { name: 'Secondary Action' });

      fireEvent.click(primaryButton);
      fireEvent.click(secondaryButton);

      expect(primaryAction).toHaveBeenCalledTimes(1);
      expect(secondaryAction).toHaveBeenCalledTimes(1);
    });

    it('should not render action buttons when actions array is empty', () => {
      const configWithoutActions: ErrorConfig = {
        title: 'Error without Actions',
        message: 'No actions available',
        severity: 'error',
        actions: [],
      };

      render(<ErrorAlert {...configWithoutActions} />);

      // Should not find any action buttons
      const buttons = screen.queryAllByRole('button');
      expect(buttons).toHaveLength(0);
    });

    it('should not render action buttons when actions is undefined', () => {
      const configWithoutActions: ErrorConfig = {
        title: 'Error without Actions',
        message: 'No actions available',
        severity: 'error',
      };

      render(<ErrorAlert {...configWithoutActions} />);

      // Should not find any action buttons
      const buttons = screen.queryAllByRole('button');
      expect(buttons).toHaveLength(0);
    });
  });

  describe('dismiss functionality', () => {
    it('should render dismiss button when onDismiss is provided', () => {
      const errorConfig: ErrorConfig = {
        title: 'Dismissible Error',
        message: 'This error can be dismissed',
        severity: 'error',
      };

      render(<ErrorAlert {...errorConfig} onDismiss={mockOnDismiss} />);

      const dismissButton = screen.getByRole('button', { name: 'Dismiss alert' });
      expect(dismissButton).toBeInTheDocument();
    });

    it('should call onDismiss when dismiss button is clicked', () => {
      const errorConfig: ErrorConfig = {
        title: 'Dismissible Error',
        message: 'This error can be dismissed',
        severity: 'error',
      };

      render(<ErrorAlert {...errorConfig} onDismiss={mockOnDismiss} />);

      const dismissButton = screen.getByRole('button', { name: 'Dismiss alert' });
      fireEvent.click(dismissButton);

      expect(mockOnDismiss).toHaveBeenCalledTimes(1);
    });

    it('should not render dismiss button when onDismiss is not provided', () => {
      const errorConfig: ErrorConfig = {
        title: 'Non-dismissible Error',
        message: 'This error cannot be dismissed',
        severity: 'error',
      };

      render(<ErrorAlert {...errorConfig} />);

      const dismissButton = screen.queryByRole('button', { name: 'Dismiss alert' });
      expect(dismissButton).not.toBeInTheDocument();
    });
  });

  describe('ARIA attributes and accessibility', () => {
    it('should have proper ARIA attributes for screen readers', () => {
      const errorConfig: ErrorConfig = {
        title: 'Accessible Error',
        message: 'This error is accessible',
        severity: 'error',
      };

      render(<ErrorAlert {...errorConfig} />);

      const container = screen.getByRole('alert');
      expect(container).toHaveAttribute('role', 'alert');
      expect(container).toHaveAttribute('aria-live', 'polite');
    });

    it('should have aria-hidden="true" on the icon', () => {
      const errorConfig: ErrorConfig = {
        title: 'Error with Icon',
        message: 'Icon should be hidden from screen readers',
        severity: 'error',
      };

      render(<ErrorAlert {...errorConfig} />);

      const icon = screen.getByRole('alert').querySelector('svg');
      expect(icon).toHaveAttribute('aria-hidden', 'true');
    });

    it('should have proper aria-label on dismiss button', () => {
      const errorConfig: ErrorConfig = {
        title: 'Dismissible Error',
        message: 'This error can be dismissed',
        severity: 'error',
      };

      render(<ErrorAlert {...errorConfig} onDismiss={mockOnDismiss} />);

      const dismissButton = screen.getByRole('button', { name: 'Dismiss alert' });
      expect(dismissButton).toHaveAttribute('aria-label', 'Dismiss alert');
    });
  });

  describe('children content rendering', () => {
    it('should render children content when provided', () => {
      const errorConfig: ErrorConfig = {
        title: 'Error with Children',
        message: 'This error has additional content',
        severity: 'error',
      };

      render(
        <ErrorAlert {...errorConfig}>
          <div data-testid="custom-content">Custom troubleshooting steps</div>
        </ErrorAlert>
      );

      expect(screen.getByTestId('custom-content')).toBeInTheDocument();
      expect(screen.getByText('Custom troubleshooting steps')).toBeInTheDocument();
    });

    it('should render without children when not provided', () => {
      const errorConfig: ErrorConfig = {
        title: 'Error without Children',
        message: 'This error has no additional content',
        severity: 'error',
      };

      render(<ErrorAlert {...errorConfig} />);

      expect(screen.getByText('Error without Children')).toBeInTheDocument();
      expect(screen.getByText('This error has no additional content')).toBeInTheDocument();
    });
  });

  describe('combined functionality', () => {
    it('should render all elements together: title, message, actions, dismiss, and children', () => {
      const fullConfig: ErrorConfig = {
        title: 'Complete Error Alert',
        message: 'This alert has all features',
        severity: 'warning',
        actions: [
          {
            label: 'Retry',
            onClick: mockActionClick,
            variant: 'primary',
          },
        ],
      };

      render(
        <ErrorAlert {...fullConfig} onDismiss={mockOnDismiss}>
          <div data-testid="troubleshooting">Troubleshooting steps here</div>
        </ErrorAlert>
      );

      // Check all elements are present
      expect(screen.getByText('Complete Error Alert')).toBeInTheDocument();
      expect(screen.getByText('This alert has all features')).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Dismiss alert' })).toBeInTheDocument();
      expect(screen.getByTestId('troubleshooting')).toBeInTheDocument();

      // Check accessibility
      const container = screen.getByRole('alert');
      expect(container).toHaveAttribute('role', 'alert');
      expect(container).toHaveAttribute('aria-live', 'polite');
    });
  });
});
