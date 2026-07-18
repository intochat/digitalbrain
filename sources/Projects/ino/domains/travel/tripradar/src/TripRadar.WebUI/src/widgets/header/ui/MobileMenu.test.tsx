import { render, screen, fireEvent } from '@testing-library/react';
import { BrowserRouter } from 'react-router-dom';
import { vi } from 'vitest';
import { MobileMenu } from './MobileMenu';

// Mock the auth store with configurable state
const mockAuthState = { isAuthenticated: false };
vi.mock('shared/store/auth', () => ({
  useAuthStore: vi.fn(selector => selector(mockAuthState)),
}));

// Mock navigation config
vi.mock('shared/config', () => ({
  NAVIGATION: [
    { name: 'Home', href: '#hero' },
    { name: 'Pricing', href: '/pricing' },
  ],
}));

vi.mock('shared/config/routes', () => ({
  ROUTES: {
    LOGIN: '/signin',
  },
}));

const renderMobileMenu = (isOpen = true, isAnimating = false, initialPath = '/') => {
  window.history.pushState({}, '', initialPath);

  const mockOnClose = vi.fn();
  return {
    ...render(
      <BrowserRouter>
        <MobileMenu isOpen={isOpen} isAnimating={isAnimating} onClose={mockOnClose} />
      </BrowserRouter>
    ),
    mockOnClose,
  };
};

describe('MobileMenu', () => {
  describe('Visual Feedback System', () => {
    it('should render close button with enhanced visual feedback', () => {
      renderMobileMenu();

      const closeButton = screen.getByRole('button', { name: /close navigation menu/i });

      // Check enhanced visual feedback classes
      expect(closeButton).toHaveClass('transition-all');
      expect(closeButton).toHaveClass('duration-[250ms]');
      expect(closeButton).toHaveClass('ease-[cubic-bezier(0.2,0,0,1)]');
      expect(closeButton).toHaveClass('active:scale-95');
      expect(closeButton).toHaveClass('focus:outline-none');
      expect(closeButton).toHaveClass('focus:ring-2');
      expect(closeButton).toHaveClass('focus:ring-primary-400/30');
    });

    it('should render navigation items with enhanced hover effects', () => {
      renderMobileMenu();

      const homeButton = screen.getByRole('menuitem', { name: /navigate to home section/i });
      const pricingLink = screen.getByRole('menuitem', { name: /navigate to pricing page/i });

      // Check enhanced visual feedback for anchor button
      expect(homeButton).toHaveClass('group');
      expect(homeButton).toHaveClass('hover:translate-x-1');
      expect(homeButton).toHaveClass('duration-[250ms]');
      expect(homeButton).toHaveClass('ease-[cubic-bezier(0.2,0,0,1)]');

      // Check enhanced visual feedback for link
      expect(pricingLink).toHaveClass('group');
      expect(pricingLink).toHaveClass('hover:translate-x-1');
      expect(pricingLink).toHaveClass('duration-[250ms]');
      expect(pricingLink).toHaveClass('ease-[cubic-bezier(0.2,0,0,1)]');
    });

    it('should render auth button with enhanced visual feedback', () => {
      renderMobileMenu();

      const loginButton = screen.getByRole('menuitem', { name: /sign in to your account/i });

      // Check login button enhanced feedback
      expect(loginButton).toHaveClass('hover:scale-[1.02]');
      expect(loginButton).toHaveClass('duration-[250ms]');
      expect(loginButton).toHaveClass('ease-[cubic-bezier(0.2,0,0,1)]');
      expect(loginButton).toHaveClass('focus:ring-2');
    });

    it('should have proper touch target sizes for all interactive elements', () => {
      renderMobileMenu();

      const closeButton = screen.getByRole('button', { name: /close navigation menu/i });
      const homeButton = screen.getByRole('menuitem', { name: /navigate to home section/i });
      const loginButton = screen.getByRole('menuitem', { name: /sign in to your account/i });

      // Check responsive minimum touch targets - starts at 40px and scales up
      expect(closeButton).toHaveStyle({ minHeight: '40px', minWidth: '40px' }); // Base size
      expect(homeButton).toHaveStyle({ minHeight: '40px' }); // Base size
      expect(loginButton).toHaveStyle({ minHeight: '40px' }); // Base size

      // Check touch-manipulation class
      expect(closeButton).toHaveClass('touch-manipulation');
      expect(homeButton).toHaveClass('touch-manipulation');
      expect(loginButton).toHaveClass('touch-manipulation');
    });

    it('should use design tokens consistently', () => {
      renderMobileMenu();

      const closeButton = screen.getByRole('button', { name: /close navigation menu/i });
      const homeButton = screen.getByRole('menuitem', { name: /navigate to home section/i });

      // Check design token usage
      expect(closeButton).toHaveClass('hover:bg-surface-accent');
      expect(closeButton).toHaveClass('dark:hover:bg-surface-accent-dark-hover');
      expect(homeButton).toHaveClass('hover:bg-surface-accent');
      expect(homeButton).toHaveClass('dark:hover:bg-surface-accent-dark-hover');
    });

    it('should handle keyboard interactions properly', () => {
      const { mockOnClose } = renderMobileMenu();

      // Test Escape key closes menu
      fireEvent.keyDown(document, { key: 'Escape' });
      expect(mockOnClose).toHaveBeenCalled();
    });

    it('should have proper tabIndex attributes for keyboard navigation', () => {
      renderMobileMenu();

      const closeButton = screen.getByRole('button', { name: /close navigation menu/i });
      const homeButton = screen.getByRole('menuitem', { name: /navigate to home section/i });
      const pricingLink = screen.getByRole('menuitem', { name: /navigate to pricing page/i });
      const loginButton = screen.getByRole('menuitem', { name: /sign in to your account/i });

      // Check all interactive elements have proper tabIndex
      expect(closeButton).toHaveAttribute('tabIndex', '0');
      expect(homeButton).toHaveAttribute('tabIndex', '0');
      expect(pricingLink).toHaveAttribute('tabIndex', '0');
      expect(loginButton).toHaveAttribute('tabIndex', '0');
    });

    it('should have proper ARIA attributes for accessibility', () => {
      renderMobileMenu();

      const mobileMenu = screen.getByRole('navigation', { name: /mobile navigation menu/i });
      const closeButton = screen.getByRole('button', { name: /close navigation menu/i });

      // Check mobile menu has proper ARIA attributes
      expect(mobileMenu).toHaveAttribute('id', 'mobile-menu');
      expect(mobileMenu).toHaveAttribute('role', 'navigation');
      expect(mobileMenu).toHaveAttribute('aria-label', 'Mobile navigation menu');
      expect(mobileMenu).toHaveAttribute('aria-hidden', 'false');
      expect(mobileMenu).toHaveAttribute('aria-modal', 'false');

      // Check close button has proper ARIA label
      expect(closeButton).toHaveAttribute('aria-label', 'Close navigation menu');
      expect(closeButton).toHaveAttribute('role', 'button');
    });

    it('should handle backdrop click to close menu', () => {
      const { mockOnClose } = renderMobileMenu();

      // Find the backdrop element (the fixed inset-0 div)
      const backdrop = document.querySelector('.fixed.inset-0.z-40');
      expect(backdrop).toBeInTheDocument();

      // Test backdrop click closes menu
      fireEvent.click(backdrop!);
      expect(mockOnClose).toHaveBeenCalled();
    });

    it('should have proper backdrop animation timing', () => {
      renderMobileMenu();

      // Find the backdrop element
      const backdrop = document.querySelector('.fixed.inset-0.z-40');
      expect(backdrop).toBeInTheDocument();

      // Check animation timing matches menu panel
      expect(backdrop).toHaveClass('transition-opacity');
      expect(backdrop).toHaveClass('duration-[250ms]');
      expect(backdrop).toHaveClass('ease-[cubic-bezier(0.2,0,0,1)]');
    });

    it('should have optimized scroll behavior for mobile menu content', () => {
      renderMobileMenu();

      // Find the scrollable content area
      const scrollableArea = document.querySelector('.flex-1.overflow-y-auto');
      expect(scrollableArea).toBeInTheDocument();

      // Check scroll optimization classes
      expect(scrollableArea).toHaveClass('overflow-y-auto');
      expect(scrollableArea).toHaveClass('overscroll-contain');

      // Check inline styles for scroll optimization
      expect(scrollableArea).toHaveStyle({
        WebkitOverflowScrolling: 'touch',
        overscrollBehavior: 'contain',
      });
    });
  });

  describe('Authentication Conditional Layout', () => {
    it('should display auth buttons when user is not authenticated', () => {
      // Set unauthenticated state
      mockAuthState.isAuthenticated = false;
      renderMobileMenu();

      // Auth buttons should be visible
      expect(screen.getByRole('menuitem', { name: /sign in to your account/i })).toBeInTheDocument();

      // Auth section should be present
      const authSection = screen.getByRole('group', { name: /authentication actions/i });
      expect(authSection).toBeInTheDocument();
    });

    it('should hide auth buttons when user is authenticated', () => {
      // Set authenticated state
      mockAuthState.isAuthenticated = true;
      renderMobileMenu();

      // Auth buttons should not be visible
      expect(screen.queryByRole('menuitem', { name: /sign in to your account/i })).not.toBeInTheDocument();

      // Auth section should not be present
      expect(screen.queryByRole('group', { name: /authentication actions/i })).not.toBeInTheDocument();
    });

    it('should position auth buttons at bottom with proper styling', () => {
      // Set unauthenticated state
      mockAuthState.isAuthenticated = false;
      renderMobileMenu();

      const authSection = screen.getByRole('group', { name: /authentication actions/i });

      // Check sticky bottom positioning
      expect(authSection).toHaveClass('sticky', 'bottom-0');
      expect(authSection).toHaveClass('border-t', 'border-outline-secondary', 'dark:border-outline-secondary-dark');
      expect(authSection).toHaveClass('bg-surface', 'dark:bg-surface-dark');
    });

    it('should maintain simple vertical list layout for navigation items', () => {
      renderMobileMenu();

      const navigationGroup = screen.getByRole('group', { name: /navigation links/i });

      // Check responsive vertical spacing between items - starts smaller and scales up
      expect(navigationGroup).toHaveClass('space-y-1.5'); // Base mobile spacing
      expect(navigationGroup).toHaveClass('xs:space-y-2'); // xs breakpoint
      expect(navigationGroup).toHaveClass('sm:space-y-2'); // sm breakpoint

      // Verify navigation items are present
      expect(screen.getByRole('menuitem', { name: /navigate to home section/i })).toBeInTheDocument();
      expect(screen.getByRole('menuitem', { name: /navigate to pricing page/i })).toBeInTheDocument();
    });

    it('should have proper touch targets for auth buttons', () => {
      // Set unauthenticated state
      mockAuthState.isAuthenticated = false;
      renderMobileMenu();

      const loginButton = screen.getByRole('menuitem', { name: /sign in to your account/i });

      // Check responsive minimum touch targets - starts at 40px and scales up
      expect(loginButton).toHaveStyle({ minHeight: '40px' }); // Base size

      // Check responsive spacing between buttons
      const authButtonContainer = loginButton.parentElement;
      expect(authButtonContainer).toHaveClass('space-y-2.5'); // Base mobile spacing
    });
  });

  describe('Interaction Debouncing', () => {
    it('should prevent interactions when animating', () => {
      const { mockOnClose } = renderMobileMenu(true, true);

      const closeButton = screen.getByRole('button', { name: /close navigation menu/i });

      // Button should be disabled when animating
      expect(closeButton).toBeDisabled();
      expect(closeButton).toHaveClass('opacity-75', 'cursor-not-allowed');

      // Click should not trigger onClose when animating
      fireEvent.click(closeButton);
      expect(mockOnClose).not.toHaveBeenCalled();
    });

    it('should prevent backdrop interactions when animating', () => {
      const { mockOnClose } = renderMobileMenu(true, true);

      const backdrop = document.querySelector('.fixed.inset-0.z-40');
      expect(backdrop).toBeInTheDocument();

      // Backdrop click should not trigger onClose when animating
      fireEvent.click(backdrop!);
      expect(mockOnClose).not.toHaveBeenCalled();
    });

    it('should prevent navigation link clicks when animating', () => {
      const { mockOnClose } = renderMobileMenu(true, true);

      const pricingLink = screen.getByRole('menuitem', { name: /navigate to pricing page/i });

      // Link click should be prevented when animating
      fireEvent.click(pricingLink);
      expect(mockOnClose).not.toHaveBeenCalled();
    });

    it('should prevent anchor button clicks when animating', () => {
      const { mockOnClose } = renderMobileMenu(true, true);

      const homeButton = screen.getByRole('menuitem', { name: /navigate to home section/i });

      // Button should be disabled when animating
      expect(homeButton).toBeDisabled();

      // Button click should not trigger onClose when animating
      fireEvent.click(homeButton);
      expect(mockOnClose).not.toHaveBeenCalled();
    });

    it('should prevent auth button clicks when animating', () => {
      const { mockOnClose } = renderMobileMenu(true, true);

      const loginButton = screen.getByRole('menuitem', { name: /sign in to your account/i });

      // Auth button clicks should be prevented when animating
      fireEvent.click(loginButton);
      expect(mockOnClose).not.toHaveBeenCalled();
    });

    it('should allow interactions when not animating', () => {
      const { mockOnClose } = renderMobileMenu(true, false);

      const closeButton = screen.getByRole('button', { name: /close navigation menu/i });

      // Button should not be disabled when not animating
      expect(closeButton).not.toBeDisabled();
      expect(closeButton).not.toHaveClass('opacity-75', 'cursor-not-allowed');

      // Click should trigger onClose when not animating
      fireEvent.click(closeButton);
      expect(mockOnClose).toHaveBeenCalled();
    });
  });
});
