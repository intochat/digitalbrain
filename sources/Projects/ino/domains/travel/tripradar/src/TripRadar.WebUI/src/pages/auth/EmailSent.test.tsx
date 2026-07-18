import { render, screen } from '@testing-library/react';
import { BrowserRouter } from 'react-router-dom';
import { useResendEmailConfirmationMutation } from 'entities/user/api';
import { EmailSent } from './EmailSent';

vi.mock('entities/user/api');

const mockUseResendEmailConfirmationMutation = vi.mocked(useResendEmailConfirmationMutation);

const renderEmailSent = () => {
  return render(
    <BrowserRouter>
      <EmailSent />
    </BrowserRouter>
  );
};

describe('EmailSent', () => {
  beforeEach(() => {
    mockUseResendEmailConfirmationMutation.mockReturnValue({
      mutateAsync: vi.fn().mockResolvedValue({}),
      isPending: false,
    } as unknown as ReturnType<typeof useResendEmailConfirmationMutation>);
  });

  describe('Accessibility and Semantic Structure', () => {
    it('should have proper semantic HTML structure', () => {
      renderEmailSent();

      // Check for main element
      expect(screen.getByRole('main')).toBeInTheDocument();

      // Check for proper heading hierarchy
      expect(screen.getByRole('heading', { level: 1, name: /check your email/i })).toBeInTheDocument();

      // Check for sections with proper labeling
      expect(screen.getByLabelText(/email confirmation/i)).toBeInTheDocument();
    });

    it('should have proper accessibility attributes for icons', () => {
      renderEmailSent();

      // Check icon has proper role and aria-label
      const iconContainer = screen.getByRole('img', { name: /email confirmation/i });
      expect(iconContainer).toBeInTheDocument();

      // Check Mail icon is hidden from screen readers
      const mailIcon = iconContainer.querySelector('svg');
      expect(mailIcon).toHaveAttribute('aria-hidden', 'true');
    });

    it('should have proper focus indicators and keyboard navigation', () => {
      renderEmailSent();

      // Check button has proper accessibility attributes
      const backButton = screen.getByRole('button', { name: /return to login page/i });
      expect(backButton).toBeInTheDocument();
      expect(backButton).toHaveAttribute('aria-label', 'Return to login page to sign in to your account');

      // Check button has focus styles
      expect(backButton).toHaveClass('focus:outline-none', 'focus:ring-2');
    });

    it('should use design tokens for proper contrast', () => {
      renderEmailSent();

      // Check main heading uses proper design token classes
      const heading = screen.getByRole('heading', { level: 1 });
      expect(heading).toHaveClass('text-content', 'dark:text-content-dark');

      // Check secondary text uses proper design token classes
      const description = screen.getByText(/we've sent you a confirmation link/i);
      expect(description).toHaveClass('text-content-secondary', 'dark:text-content-secondary-dark');

      // Check button uses proper design token classes
      const button = screen.getByRole('button', { name: /return to login page/i });
      expect(button).toHaveClass('bg-button', 'dark:bg-button-dark', 'text-button-text', 'dark:text-button-text-dark');
    });

    it('should have proper content structure and messaging', () => {
      renderEmailSent();

      // Check essential content is present
      expect(screen.getByRole('heading', { name: /check your email/i })).toBeInTheDocument();
      expect(screen.getByText(/we've sent you a confirmation link/i)).toBeInTheDocument();
      expect(screen.getByText(/didn't receive the email/i)).toBeInTheDocument();
      expect(screen.getByRole('link', { name: /open help center/i })).toBeInTheDocument();
      expect(screen.getByRole('link', { name: /use a different email/i })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: /return to login page/i })).toBeInTheDocument();
    });
  });
});
