import { render, screen, fireEvent } from '@testing-library/react';
import { BrowserRouter } from 'react-router-dom';
import { vi } from 'vitest';
import { Navigation } from './Navigation';

// Mock the navigation config
vi.mock('shared/config', () => ({
  NAVIGATION: [
    { name: 'Home', href: '#hero' },
    { name: 'Pricing', href: '/pricing' },
  ],
}));

const renderNavigation = (initialPath = '/') => {
  window.history.pushState({}, '', initialPath);
  return render(
    <BrowserRouter>
      <Navigation />
    </BrowserRouter>
  );
};

describe('Navigation', () => {
  describe('Accessibility and Design Tokens', () => {
    it('should render navigation with proper ARIA attributes', () => {
      renderNavigation();

      const nav = screen.getByRole('navigation');
      expect(nav).toHaveAttribute('aria-label', 'Main navigation');
    });

    it('should render anchor buttons with proper accessibility labels', () => {
      renderNavigation();

      const homeButton = screen.getByRole('button', { name: /navigate to home section/i });

      expect(homeButton).toBeInTheDocument();
    });

    it('should render links with proper aria-current for active state', () => {
      renderNavigation('/pricing');

      const pricingLink = screen.getByRole('link', { name: /navigate to pricing page/i });
      expect(pricingLink).toHaveAttribute('aria-current', 'page');
    });

    it('should have focus indicators using design tokens', () => {
      renderNavigation();

      const homeButton = screen.getByRole('button', { name: /navigate to home section/i });
      expect(homeButton).toHaveClass('focus:outline-none', 'focus:text-content', 'dark:focus:text-content-dark');
    });

    it('should have minimum touch target size for accessibility', () => {
      renderNavigation('/pricing');

      const homeButton = screen.getByRole('button', { name: /navigate to home section/i });
      const pricingLink = screen.getByRole('link', { name: /navigate to pricing page/i });

      // Check minimum 44px touch targets and touch-manipulation
      expect(homeButton).toHaveClass('min-h-11', 'touch-manipulation');
      expect(homeButton).toHaveStyle({ minHeight: '44px' });

      expect(pricingLink).toHaveClass('min-h-11', 'touch-manipulation');
      expect(pricingLink).toHaveStyle({ minHeight: '44px' });
    });
  });

  describe('Interactive States', () => {
    it('should have hover states using design tokens', () => {
      renderNavigation();

      const homeButton = screen.getByRole('button', { name: /navigate to home section/i });
      expect(homeButton).toHaveClass('hover:text-content', 'dark:hover:text-content-dark');
    });

    it('should handle anchor link clicks', () => {
      // Mock scrollIntoView
      const mockScrollIntoView = vi.fn();
      const mockQuerySelector = vi.spyOn(document, 'querySelector').mockReturnValue({
        scrollIntoView: mockScrollIntoView,
      } as unknown);

      renderNavigation('/');

      const homeButton = screen.getByRole('button', { name: /navigate to home section/i });
      fireEvent.click(homeButton);

      expect(mockQuerySelector).toHaveBeenCalledWith('#hero');
      expect(mockScrollIntoView).toHaveBeenCalledWith({ behavior: 'smooth' });

      mockQuerySelector.mockRestore();
    });
  });

  describe('Design Token Compliance', () => {
    it('should use design tokens for text colors', () => {
      renderNavigation();

      const homeButton = screen.getByRole('button', { name: /navigate to home section/i });
      expect(homeButton).toHaveClass('text-content-secondary', 'dark:text-content-secondary-dark');
    });

    it('should use design tokens for active state styling', () => {
      renderNavigation('/pricing');

      const pricingLink = screen.getByRole('link', { name: /navigate to pricing page/i });
      expect(pricingLink).toHaveClass('text-content', 'dark:text-content-dark');
    });
  });
});
