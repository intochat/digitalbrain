import { renderHook, act } from '@testing-library/react';
import { vi } from 'vitest';
import { useMobileMenuState } from './useMobileMenuState';

// Mock timers for testing debouncing
vi.useFakeTimers();

describe('useMobileMenuState', () => {
  afterEach(() => {
    vi.clearAllTimers();
  });

  afterAll(() => {
    vi.useRealTimers();
  });

  describe('Interaction Debouncing', () => {
    it('should debounce rapid menu toggle interactions', () => {
      const { result } = renderHook(() => useMobileMenuState());

      // Initially menu should be closed and not animating
      expect(result.current.isMenuOpen).toBe(false);
      expect(result.current.isAnimating).toBe(false);

      // First toggle should be debounced
      act(() => {
        result.current.handleMenuToggle();
      });

      // Should still be closed immediately after call (debounced)
      expect(result.current.isMenuOpen).toBe(false);
      expect(result.current.isAnimating).toBe(false);

      // Fast forward through debounce delay (100ms)
      act(() => {
        vi.advanceTimersByTime(100);
      });

      // Now should be open and animating
      expect(result.current.isMenuOpen).toBe(true);
      expect(result.current.isAnimating).toBe(true);

      // Rapid second toggle should be ignored while animating
      act(() => {
        result.current.handleMenuToggle();
      });

      // Should still be open and animating (ignored)
      expect(result.current.isMenuOpen).toBe(true);
      expect(result.current.isAnimating).toBe(true);

      // Fast forward through animation duration (300ms)
      act(() => {
        vi.advanceTimersByTime(300);
      });

      // Should no longer be animating
      expect(result.current.isMenuOpen).toBe(true);
      expect(result.current.isAnimating).toBe(false);
    });

    it('should debounce rapid menu close interactions', () => {
      const { result } = renderHook(() => useMobileMenuState());

      // First open the menu
      act(() => {
        result.current.handleMenuToggle();
        vi.advanceTimersByTime(100);
        vi.advanceTimersByTime(300);
      });

      expect(result.current.isMenuOpen).toBe(true);
      expect(result.current.isAnimating).toBe(false);

      // Close should be debounced with shorter delay (50ms)
      act(() => {
        result.current.handleMenuClose();
      });

      // Should still be open immediately after call (debounced)
      expect(result.current.isMenuOpen).toBe(true);
      expect(result.current.isAnimating).toBe(false);

      // Fast forward through close debounce delay (50ms)
      act(() => {
        vi.advanceTimersByTime(50);
      });

      // Now should be closed and animating
      expect(result.current.isMenuOpen).toBe(false);
      expect(result.current.isAnimating).toBe(true);

      // Rapid second close should be ignored while animating
      act(() => {
        result.current.handleMenuClose();
      });

      // Should still be closed and animating (ignored)
      expect(result.current.isMenuOpen).toBe(false);
      expect(result.current.isAnimating).toBe(true);

      // Fast forward through animation duration (300ms)
      act(() => {
        vi.advanceTimersByTime(300);
      });

      // Should no longer be animating
      expect(result.current.isMenuOpen).toBe(false);
      expect(result.current.isAnimating).toBe(false);
    });

    it('should prevent multiple rapid toggles from causing conflicts', () => {
      const { result } = renderHook(() => useMobileMenuState());

      // Rapid multiple toggles
      act(() => {
        result.current.handleMenuToggle();
        result.current.handleMenuToggle();
        result.current.handleMenuToggle();
      });

      // Should still be closed (all ignored except last)
      expect(result.current.isMenuOpen).toBe(false);
      expect(result.current.isAnimating).toBe(false);

      // Fast forward through debounce delay
      act(() => {
        vi.advanceTimersByTime(100);
      });

      // Should be open (only last toggle took effect)
      expect(result.current.isMenuOpen).toBe(true);
      expect(result.current.isAnimating).toBe(true);
    });

    it('should cleanup timeouts properly', () => {
      const { result, unmount } = renderHook(() => useMobileMenuState());

      // Start a toggle
      act(() => {
        result.current.handleMenuToggle();
      });

      // Cleanup should clear timeouts
      act(() => {
        result.current.cleanup();
      });

      // Fast forward - should not change state since timeouts were cleared
      act(() => {
        vi.advanceTimersByTime(100);
      });

      expect(result.current.isMenuOpen).toBe(false);
      expect(result.current.isAnimating).toBe(false);

      // Unmount should also cleanup
      unmount();
    });

    it('should ensure state consistency during rapid interactions', () => {
      const { result } = renderHook(() => useMobileMenuState());

      // Open menu
      act(() => {
        result.current.handleMenuToggle();
        vi.advanceTimersByTime(100);
      });

      expect(result.current.isMenuOpen).toBe(true);
      expect(result.current.isAnimating).toBe(true);

      // Try to close while still animating (should be ignored)
      act(() => {
        result.current.handleMenuClose();
      });

      // State should remain consistent
      expect(result.current.isMenuOpen).toBe(true);
      expect(result.current.isAnimating).toBe(true);

      // Complete animation
      act(() => {
        vi.advanceTimersByTime(300);
      });

      // Now should be able to close
      expect(result.current.isAnimating).toBe(false);

      act(() => {
        result.current.handleMenuClose();
        vi.advanceTimersByTime(50);
      });

      expect(result.current.isMenuOpen).toBe(false);
      expect(result.current.isAnimating).toBe(true);
    });
  });
});
