import { useState, useCallback, useRef } from 'react';

/**
 * Custom hook for managing mobile menu state with performance optimizations and interaction debouncing
 * Includes debouncing to prevent animation conflicts from rapid user interactions
 */
export const useMobileMenuState = () => {
  const [isMenuOpen, setIsMenuOpen] = useState(false);
  const [isAnimating, setIsAnimating] = useState(false);
  const debounceTimeoutRef = useRef<NodeJS.Timeout | null>(null);
  const animationTimeoutRef = useRef<NodeJS.Timeout | null>(null);

  // Debounced menu toggle to prevent rapid interactions and animation conflicts
  const handleMenuToggle = useCallback(() => {
    // Prevent new interactions while animating or during debounce period
    if (isAnimating) {
      return;
    }

    // Clear any existing debounce timeout
    if (debounceTimeoutRef.current) {
      clearTimeout(debounceTimeoutRef.current);
    }

    // Debounce rapid interactions with 100ms delay
    debounceTimeoutRef.current = setTimeout(() => {
      setIsAnimating(true);
      setIsMenuOpen(prev => !prev);

      // Clear animation state after animation completes (250ms + buffer)
      if (animationTimeoutRef.current) {
        clearTimeout(animationTimeoutRef.current);
      }

      animationTimeoutRef.current = setTimeout(() => {
        setIsAnimating(false);
      }, 300); // 250ms animation + 50ms buffer
    }, 100);
  }, [isAnimating]);

  // Debounced menu close to ensure state consistency
  const handleMenuClose = useCallback(() => {
    // Prevent new interactions while animating
    if (isAnimating) {
      return;
    }

    // Clear any existing debounce timeout
    if (debounceTimeoutRef.current) {
      clearTimeout(debounceTimeoutRef.current);
    }

    // Debounce rapid close interactions with 50ms delay (shorter for close actions)
    debounceTimeoutRef.current = setTimeout(() => {
      setIsAnimating(true);
      setIsMenuOpen(false);

      // Clear animation state after animation completes
      if (animationTimeoutRef.current) {
        clearTimeout(animationTimeoutRef.current);
      }

      animationTimeoutRef.current = setTimeout(() => {
        setIsAnimating(false);
      }, 300); // 250ms animation + 50ms buffer
    }, 50);
  }, [isAnimating]);

  // Cleanup timeouts on unmount
  const cleanup = useCallback(() => {
    if (debounceTimeoutRef.current) {
      clearTimeout(debounceTimeoutRef.current);
      debounceTimeoutRef.current = null;
    }
    if (animationTimeoutRef.current) {
      clearTimeout(animationTimeoutRef.current);
      animationTimeoutRef.current = null;
    }
  }, []);

  // IMPORTANT: No body scroll locking - page scroll remains unblocked as per requirements 5.5
  // The mobile menu slides down and allows page scrolling while maintaining smooth internal scrolling
  // This ensures users can scroll the page content even when the mobile menu is open

  return {
    isMenuOpen,
    isAnimating,
    handleMenuToggle,
    handleMenuClose,
    cleanup,
  };
};
