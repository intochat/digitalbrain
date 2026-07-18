import { render, screen, fireEvent } from '@testing-library/react';
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { ThemeProvider } from 'app/providers/ThemeContext';
import { ThemeToggle } from './ThemeToggle';

// Mock localStorage
const localStorageMock = {
  getItem: vi.fn(),
  setItem: vi.fn(),
  removeItem: vi.fn(),
  clear: vi.fn(),
};
Object.defineProperty(window, 'localStorage', {
  value: localStorageMock,
});

const renderThemeToggle = () => {
  return render(
    <ThemeProvider>
      <ThemeToggle />
    </ThemeProvider>
  );
};

describe('ThemeToggle', () => {
  beforeEach(() => {
    localStorageMock.getItem.mockClear();
    localStorageMock.setItem.mockClear();
    document.documentElement.classList.remove('dark');
  });

  it('should render with sun icon in light mode', () => {
    localStorageMock.getItem.mockReturnValue('light');
    renderThemeToggle();

    const button = screen.getByRole('button', { name: /switch to dark mode/i });
    expect(button).toBeInTheDocument();

    // Check for sun icon (lucide-react icons have specific attributes)
    const sunIcon = button.querySelector('svg');
    expect(sunIcon).toBeInTheDocument();
  });

  it('should render with moon icon in dark mode', () => {
    localStorageMock.getItem.mockReturnValue('dark');
    renderThemeToggle();

    const button = screen.getByRole('button', { name: /switch to light mode/i });
    expect(button).toBeInTheDocument();

    // Check for moon icon
    const moonIcon = button.querySelector('svg');
    expect(moonIcon).toBeInTheDocument();
  });

  it('should toggle between light and dark themes', () => {
    localStorageMock.getItem.mockReturnValue('light');
    renderThemeToggle();

    const button = screen.getByRole('button');

    // Initially in light mode
    expect(button).toHaveAttribute('aria-label', 'Switch to dark mode');

    // Click to switch to dark mode
    fireEvent.click(button);

    // Should now be in dark mode
    expect(button).toHaveAttribute('aria-label', 'Switch to light mode');

    // Click again to switch back to light mode
    fireEvent.click(button);

    // Should be back to light mode
    expect(button).toHaveAttribute('aria-label', 'Switch to dark mode');
  });

  it('should use design tokens for styling', () => {
    renderThemeToggle();

    const button = screen.getByRole('button');

    // Check that the button has design token classes for clean text-only styling
    expect(button).toHaveClass('text-content-secondary');
    expect(button).toHaveClass('dark:text-content-secondary-dark');
    expect(button).toHaveClass('hover:text-content');
    expect(button).toHaveClass('dark:hover:text-content-dark');
    expect(button).toHaveClass('focus:text-content');
    expect(button).toHaveClass('dark:focus:text-content-dark');
  });

  it('should persist theme selection to localStorage', () => {
    localStorageMock.getItem.mockReturnValue('light');
    renderThemeToggle();

    const button = screen.getByRole('button');
    fireEvent.click(button);

    // Should save dark theme to localStorage
    expect(localStorageMock.setItem).toHaveBeenCalledWith('tripradar-theme', 'dark');
  });

  it('should default to light theme when localStorage is empty', () => {
    localStorageMock.getItem.mockReturnValue(null);
    renderThemeToggle();

    const button = screen.getByRole('button');
    expect(button).toHaveAttribute('aria-label', 'Switch to dark mode');
  });
});
