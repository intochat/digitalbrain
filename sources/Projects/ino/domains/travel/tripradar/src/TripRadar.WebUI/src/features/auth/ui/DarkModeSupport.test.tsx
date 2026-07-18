/**
 * Dark Mode Support Tests
 *
 * Tests to verify that all auth components have proper dark mode support
 * and use design tokens consistently.
 *
 * **Property 4: Dark mode support**
 * **Validates: Requirements 3.4**
 */

import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import { BrowserRouter } from 'react-router-dom';
import { ErrorAlert } from './ErrorAlert';
import { OAuthButtons } from './OAuthButtons';
import { Signup } from './Signup';

// Test wrapper with required providers
const TestWrapper = ({ children }: { children: React.ReactNode }) => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });

  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>{children}</BrowserRouter>
    </QueryClientProvider>
  );
};

describe('Dark Mode Support', () => {
  describe('Property 4: Dark mode support', () => {
    it('should have dark mode classes for all visual elements in Signup component', () => {
      render(
        <TestWrapper>
          <Signup />
        </TestWrapper>
      );

      // Check background elements have dark mode variants
      const backgroundElements = document.querySelectorAll('[class*="bg-surface"]:not([class*="bg-gradient"])');
      backgroundElements.forEach(element => {
        expect(element.className).toMatch(/dark:bg-surface/);
      });

      // Check primary text elements have dark mode variants (not muted text)
      const primaryTextElements = document.querySelectorAll(
        '[class*="text-content"]:not([class*="text-content-muted"]):not([class*="text-content-secondary"])'
      );
      primaryTextElements.forEach(element => {
        expect(element.className).toMatch(/dark:text-content-dark/);
      });

      // Check secondary text elements have dark mode variants
      const secondaryTextElements = document.querySelectorAll('[class*="text-content-secondary"]');
      secondaryTextElements.forEach(element => {
        expect(element.className).toMatch(/dark:text-content-secondary-dark/);
      });

      // Check border elements have dark mode variants
      const borderElements = document.querySelectorAll(
        '[class*="border-outline"]:not([class*="border-t"]):not([class*="border-l"]):not([class*="border-r"]):not([class*="border-b"])'
      );
      borderElements.forEach(element => {
        expect(element.className).toMatch(/dark:border-outline/);
      });
    });

    it('should use design tokens consistently without hardcoded colors', () => {
      render(
        <TestWrapper>
          <Signup />
        </TestWrapper>
      );

      // Get all elements with class attributes
      const allElements = document.querySelectorAll('*[class]');

      allElements.forEach(element => {
        const className = element.className || '';

        // Skip elements without class names
        if (!className || typeof className !== 'string') return;

        // Check that no hardcoded color classes are used (except for specific status colors)
        const hardcodedColorPatterns = [
          /bg-blue-\d+/,
          /bg-gray-\d+/,
          /bg-green-\d+(?!00)/, // Allow green-100 for status
          /bg-red-\d+(?!00)/, // Allow red-100 for status
          /text-blue-\d+/,
          /text-gray-\d+/,
          /border-blue-\d+/,
          /border-gray-\d+/,
        ];

        hardcodedColorPatterns.forEach(pattern => {
          expect(className).not.toMatch(pattern);
        });

        // If element has background color, it should have dark variant
        if (className.includes('bg-') && !className.includes('bg-gradient') && !className.includes('bg-primary')) {
          expect(className).toMatch(/dark:bg-/);
        }

        // If element has text color (excluding utility classes and muted text), it should have dark variant
        if (
          className.includes('text-content') &&
          !className.includes('text-content-muted') &&
          !className.includes('text-base') &&
          !className.includes('text-sm') &&
          !className.includes('text-xs') &&
          !className.includes('text-center')
        ) {
          expect(className).toMatch(/dark:text-/);
        }

        // If element has border color, it should have dark variant
        if (
          className.includes('border-') &&
          !className.includes('border-t') &&
          !className.includes('border-l') &&
          !className.includes('border-r') &&
          !className.includes('border-b')
        ) {
          expect(className).toMatch(/dark:border-/);
        }
      });
    });

    it('should have consistent icon usage with Lucide React', () => {
      render(
        <TestWrapper>
          <Signup />
        </TestWrapper>
      );

      // Check that Lucide icons are used (they have specific SVG structure)
      const mailIcon = document.querySelector('svg[class*="h-4 w-4"]');
      expect(mailIcon).toBeInTheDocument();

      // Verify no FontAwesome icons are present
      const fontAwesomeIcons = document.querySelectorAll('[class*="fa-"]');
      expect(fontAwesomeIcons).toHaveLength(0);
    });
  });

  describe('ErrorAlert Dark Mode Support', () => {
    it('should have proper dark mode support for all severity levels', () => {
      const { rerender } = render(<ErrorAlert title="Test Error" message="Test message" severity="error" />);

      // Check error severity
      let alertElement = screen.getByRole('alert');
      expect(alertElement.className).toMatch(/dark:bg-surface-accent-dark/);
      expect(alertElement.className).toMatch(/dark:border-outline-dark/);

      // Check warning severity
      rerender(<ErrorAlert title="Test Warning" message="Test message" severity="warning" />);

      alertElement = screen.getByRole('alert');
      expect(alertElement.className).toMatch(/dark:bg-surface-accent-dark/);

      // Check info severity
      rerender(<ErrorAlert title="Test Info" message="Test message" severity="info" />);

      alertElement = screen.getByRole('alert');
      expect(alertElement.className).toMatch(/dark:bg-surface-accent-dark/);
    });

    it('should use Lucide icons instead of FontAwesome', () => {
      render(<ErrorAlert title="Test Error" message="Test message" severity="error" />);

      // Check that Lucide icons are used (they don't have fa- classes)
      const iconElements = document.querySelectorAll('[class*="fa-"]');
      expect(iconElements).toHaveLength(0);

      // Check that SVG icons are present
      const svgIcons = document.querySelectorAll('svg');
      expect(svgIcons.length).toBeGreaterThan(0);
    });
  });

  describe('OAuthButtons Dark Mode Support', () => {
    it('should have proper dark mode classes', () => {
      render(<OAuthButtons />);

      const oauthButton = screen.getByRole('button');
      expect(oauthButton.className).toMatch(/dark:border-outline-dark/);
      expect(oauthButton.className).toMatch(/dark:text-content-dark/);
      expect(oauthButton.className).toMatch(/dark:bg-surface-dark/);
      expect(oauthButton.className).toMatch(/dark:hover:bg-surface-accent-dark/);
    });
  });

  describe('Theme Consistency', () => {
    it('should use consistent design token patterns across components', () => {
      render(
        <TestWrapper>
          <div>
            <Signup />
            <ErrorAlert title="Test" message="Test" severity="info" />
            <OAuthButtons />
          </div>
        </TestWrapper>
      );

      // Check that all components use the same design token patterns
      const surfaceElements = document.querySelectorAll('[class*="bg-surface"]');
      surfaceElements.forEach(element => {
        // Should use design tokens, not hardcoded colors
        expect(element.className).toMatch(/bg-surface/);
        expect(element.className).toMatch(/dark:bg-surface/);
      });

      const contentElements = document.querySelectorAll('[class*="text-content"]:not([class*="text-content-muted"])');
      contentElements.forEach(element => {
        expect(element.className).toMatch(/text-content/);
        if (!element.className.includes('text-content-muted')) {
          expect(element.className).toMatch(/dark:text-content/);
        }
      });

      const outlineElements = document.querySelectorAll('[class*="border-outline"]');
      outlineElements.forEach(element => {
        expect(element.className).toMatch(/border-outline/);
        expect(element.className).toMatch(/dark:border-outline/);
      });
    });

    it('should have consistent spacing and sizing across components', () => {
      render(
        <TestWrapper>
          <Signup />
        </TestWrapper>
      );

      // Check for consistent rounded corners
      const roundedElements = document.querySelectorAll('[class*="rounded-xl"]');
      expect(roundedElements.length).toBeGreaterThan(0);

      // Check for consistent padding
      const paddedElements = document.querySelectorAll('[class*="p-"]');
      expect(paddedElements.length).toBeGreaterThan(0);

      // Check for consistent spacing
      const spacedElements = document.querySelectorAll('[class*="space-y-"]');
      expect(spacedElements.length).toBeGreaterThan(0);
    });
  });
});
