import { useState, useEffect, useRef, useCallback } from 'react';

export const useScrollDetection = () => {
  const [isScrolled, setIsScrolled] = useState(false);
  const sentinelRef = useRef<HTMLDivElement>(null);
  const throttleRef = useRef<number | null>(null);

  // Throttled callback to prevent excessive state updates
  const throttledSetIsScrolled = useCallback((value: boolean) => {
    if (throttleRef.current) {
      cancelAnimationFrame(throttleRef.current);
    }

    throttleRef.current = requestAnimationFrame(() => {
      setIsScrolled(value);
      throttleRef.current = null;
    });
  }, []);

  useEffect(() => {
    const observer = new IntersectionObserver(
      ([entry]) => {
        throttledSetIsScrolled(!entry.isIntersecting);
      },
      {
        threshold: 0,
        rootMargin: '0px 0px -1px 0px', // Slight offset for better detection
      }
    );

    if (sentinelRef.current) {
      observer.observe(sentinelRef.current);
    }

    return () => {
      observer.disconnect();
      if (throttleRef.current) {
        cancelAnimationFrame(throttleRef.current);
      }
    };
  }, [throttledSetIsScrolled]);

  return { isScrolled, sentinelRef };
};
