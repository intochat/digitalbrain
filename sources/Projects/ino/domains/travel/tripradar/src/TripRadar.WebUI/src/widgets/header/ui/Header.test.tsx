import { render, screen } from '@testing-library/react';
import { BrowserRouter } from 'react-router-dom';
import { vi } from 'vitest';
import { Header } from './Header';

// Mock the hooks and components
vi.mock('shared/lib/hooks', () => ({
  useScrollDetection: () => ({
    sentinelRef: { current: null },
    isScrolled: false,
  }),
}));

vi.mock('../lib', () => ({
  useMobileMenuState: () => ({
    isMenuOpen: false,
    isAnimating: false,
    handleMenuToggle: vi.fn(),
    handleMenuClose: vi.fn(),
    cleanup: vi.fn(),
  }),
}));

vi.mock('shared/ui', () => ({
  ThemeToggle: () => <div data-testid="theme-toggle">Theme Toggle</div>,
}));

vi.mock('./Logo', () => ({
  Logo: () => <div data-testid="logo">Logo</div>,
}));

vi.mock('./Navigation', () => ({
  Navigation: () => <div data-testid="navigation">Navigation</div>,
}));

vi.mock('./UserActions', () => ({
  UserActions: () => <div data-testid="user-actions">User Actions</div>,
}));

vi.mock('./MobileMenu', () => ({
  MobileMenu: ({ isOpen, isAnimating }: { isOpen: boolean; isAnimating?: boolean }) => (
    <div data-testid="mobile-menu" data-open={isOpen} data-animating={isAnimating}>
      Mobile Menu
    </div>
  ),
}));

const renderHeader = () => {
  return render(
    <BrowserRouter>
      <Header />
    </BrowserRouter>
  );
};

describe('Header', () => {
  describe('Visual Feedback System', () => {
    it('should render burger menu button with enhanced visual feedback classes', () => {
      renderHeader();

      const burgerButton = screen.getByRole('button', { name: /open navigation menu/i });

      // Check that enhanced visual feedback classes are present
      expect(burgerButton).toHaveClass('transition-all');
      expect(burgerButton).toHaveClass('duration-[250ms]');
      expect(burgerButton).toHaveClass('ease-[cubic-bezier(0.2,0,0,1)]');
      expect(burgerButton).toHaveClass('active:scale-95');
      expect(burgerButton).toHaveClass('focus:outline-none');
      expect(burgerButton).toHaveClass('focus:ring-2');
      expect(burgerButton).toHaveClass('focus:ring-primary-400/30');
    });

    it('should have proper touch target size for burger menu button', () => {
      renderHeader();

      const burgerButton = screen.getByRole('button', { name: /open navigation menu/i });

      // Check responsive minimum touch targets - starts at 40px (min-w-10 min-h-10) and scales up
      expect(burgerButton).toHaveClass('min-w-10', 'min-h-10'); // Base mobile size
      expect(burgerButton).toHaveClass('xs:min-w-11', 'xs:min-h-11'); // xs breakpoint
      expect(burgerButton).toHaveClass('sm:min-w-11', 'sm:min-h-11'); // sm breakpoint
      expect(burgerButton).toHaveStyle({ minWidth: '40px', minHeight: '40px' }); // Base size
      expect(burgerButton).toHaveClass('touch-manipulation');
    });

    it('should have proper ARIA attributes for accessibility', () => {
      renderHeader();

      const burgerButton = screen.getByRole('button', { name: /open navigation menu/i });

      expect(burgerButton).toHaveAttribute('aria-label', 'Open navigation menu');
      expect(burgerButton).toHaveAttribute('aria-expanded', 'false');
      expect(burgerButton).toHaveAttribute('aria-controls', 'mobile-menu');
      expect(burgerButton).toHaveAttribute('aria-haspopup', 'true');
      expect(burgerButton).toHaveAttribute('role', 'button');
      expect(burgerButton).toHaveAttribute('tabIndex', '0');
    });

    it('should use design tokens for colors and spacing', () => {
      renderHeader();

      const burgerButton = screen.getByRole('button', { name: /open navigation menu/i });

      // Check that design tokens are used instead of hardcoded colors
      expect(burgerButton).toHaveClass('hover:bg-surface-accent');
      expect(burgerButton).toHaveClass('dark:hover:bg-surface-accent-dark-hover');
      expect(burgerButton).toHaveClass('text-content-secondary');
      expect(burgerButton).toHaveClass('dark:text-content-secondary-dark');
    });

    it('should have consistent animation timing across all interactive elements', () => {
      renderHeader();

      const burgerButton = screen.getByRole('button', { name: /open navigation menu/i });
      const menuIcon = burgerButton.querySelector('svg');

      // Check that both button and icon use consistent animation timing
      expect(burgerButton).toHaveClass('duration-[250ms]');
      expect(burgerButton).toHaveClass('ease-[cubic-bezier(0.2,0,0,1)]');
      expect(menuIcon).toHaveClass('duration-[250ms]');
      expect(menuIcon).toHaveClass('ease-[cubic-bezier(0.2,0,0,1)]');
    });
  });
});
