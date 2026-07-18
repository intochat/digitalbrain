import { useEffect, useRef, useState } from 'react';
import { X, ChevronRight } from 'lucide-react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { useFrontendLanguage } from 'app/providers/FrontendLanguageContext';
import { NAVIGATION } from 'shared/config';
import { ROUTES } from 'shared/config/routes';
import { cn } from 'shared/lib/utils';
import { useAuthStore } from 'shared/store/auth';

interface MobileMenuProps {
  isOpen: boolean;
  isAnimating?: boolean;
  onClose: () => void;
}

const scrollToSection = (href: string) => {
  const element = document.querySelector(href);
  element?.scrollIntoView({ behavior: 'smooth' });
};

const handleAnchorClick = (href: string, navigate: ReturnType<typeof useNavigate>, currentPath: string) => {
  if (currentPath === '/') {
    scrollToSection(href);
  } else {
    navigate('/' + href);
  }
};

export const MobileMenu = ({ isOpen, isAnimating = false, onClose }: MobileMenuProps) => {
  const isAuthenticated = useAuthStore(state => state.isAuthenticated);
  const location = useLocation();
  const navigate = useNavigate();
  const { t } = useFrontendLanguage();
  const menuRef = useRef<HTMLDivElement>(null);
  const closeButtonRef = useRef<HTMLButtonElement>(null);
  const [focusedIndex, setFocusedIndex] = useState(-1);

  // Get all focusable elements for keyboard navigation
  const getFocusableElements = () => {
    if (!menuRef.current) return [];

    const focusableSelectors = [
      'button:not([disabled])',
      'a[href]:not([disabled])',
      'input:not([disabled])',
      'select:not([disabled])',
      'textarea:not([disabled])',
      '[tabindex]:not([tabindex="-1"]):not([disabled])',
    ].join(', ');

    return Array.from(menuRef.current.querySelectorAll(focusableSelectors)) as HTMLElement[];
  };

  // Enhanced keyboard support with logical tab order and focus management
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        onClose();
        return;
      }

      // Handle Tab navigation within mobile menu
      if (e.key === 'Tab') {
        e.preventDefault();
        const focusableElements = getFocusableElements();

        if (focusableElements.length === 0) return;

        let nextIndex;
        if (e.shiftKey) {
          // Shift+Tab: move backwards
          nextIndex = focusedIndex <= 0 ? focusableElements.length - 1 : focusedIndex - 1;
        } else {
          // Tab: move forwards
          nextIndex = focusedIndex >= focusableElements.length - 1 ? 0 : focusedIndex + 1;
        }

        setFocusedIndex(nextIndex);
        focusableElements[nextIndex]?.focus();
      }

      // Handle Arrow key navigation for better mobile accessibility
      if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
        e.preventDefault();
        const focusableElements = getFocusableElements();

        if (focusableElements.length === 0) return;

        let nextIndex;
        if (e.key === 'ArrowUp') {
          nextIndex = focusedIndex <= 0 ? focusableElements.length - 1 : focusedIndex - 1;
        } else {
          nextIndex = focusedIndex >= focusableElements.length - 1 ? 0 : focusedIndex + 1;
        }

        setFocusedIndex(nextIndex);
        focusableElements[nextIndex]?.focus();
      }
    };

    if (isOpen) {
      document.addEventListener('keydown', handleKeyDown);
    }

    return () => {
      document.removeEventListener('keydown', handleKeyDown);
    };
  }, [isOpen, onClose, focusedIndex]);

  // Focus management when menu opens/closes
  useEffect(() => {
    if (isOpen) {
      // Focus the close button when menu opens for immediate keyboard access
      setTimeout(() => {
        closeButtonRef.current?.focus();
        setFocusedIndex(0);
      }, 100); // Small delay to ensure animation starts
    } else {
      // Reset focus index when menu closes
      setFocusedIndex(-1);
    }
  }, [isOpen]);

  if (!isOpen) return null;

  const navigation = NAVIGATION;

  return (
    <>
      {/* Full-screen backdrop with proper z-index layering and hardware acceleration */}
      <div
        className={cn(
          'fixed inset-0 z-40 md:hidden',
          // Hardware-accelerated opacity animation with transform3d for GPU layer
          'transition-opacity duration-[250ms] ease-[cubic-bezier(0.2,0,0,1)]',
          'bg-black/40 dark:bg-black/70 backdrop-blur-sm',
          // Force hardware acceleration with transform3d
          'transform-gpu will-change-[opacity]',
          isOpen ? 'opacity-100' : 'opacity-0 pointer-events-none'
        )}
        style={{
          // Force hardware acceleration by creating a new composite layer
          transform: 'translate3d(0, 0, 0)',
          // Optimize for opacity changes
          willChange: isOpen ? 'opacity' : 'auto',
        }}
        onClick={e => {
          // Prevent interactions during animation to avoid conflicts
          if (isAnimating) return;

          // Prevent event bubbling and ensure clean backdrop interaction
          e.stopPropagation();
          onClose();
        }}
        onTouchEnd={e => {
          // Prevent interactions during animation to avoid conflicts
          if (isAnimating) return;

          // Handle touch events for mobile devices - ensure smooth backdrop tap
          // Only prevent default if the touch target is the backdrop itself
          if (e.target === e.currentTarget) {
            e.preventDefault();
            e.stopPropagation();
            onClose();
          }
        }}
        aria-hidden="true"
      />

      {/* Slide-down menu panel - full-width mobile-first positioning with hardware acceleration and landscape optimization */}
      <div
        ref={menuRef}
        id="mobile-menu"
        className={cn(
          // Mobile-first positioning and sizing - slide down from header with landscape optimization
          // Responsive positioning to match header height changes across all breakpoints
          'fixed top-14 xs:top-16 sm:top-18 landscape:top-12 landscape:xs:top-14 landscape:sm:top-16 left-0 right-0 z-50 md:hidden',
          'w-full', // Full width as per requirements (100% viewport width)
          // Responsive height calculation to match header height with landscape optimization
          'h-[calc(100vh-3rem)] xs:h-[calc(100vh-3.5rem)] sm:h-[calc(100vh-4rem)]', // Full height minus header
          'landscape:h-[calc(100vh-2.5rem)] landscape:xs:h-[calc(100vh-3rem)] landscape:sm:h-[calc(100vh-3.5rem)]', // Landscape adjustments
          // Mobile-first styling
          'bg-surface dark:bg-surface-dark',
          'border-t border-outline-secondary dark:border-outline-secondary-dark',
          'shadow-2xl',
          // Hardware-accelerated transform animation with GPU optimization
          'transition-transform duration-[250ms] ease-[cubic-bezier(0.2,0,0,1)]',
          'transform-gpu will-change-[transform]',
          isOpen ? 'translate-y-0' : '-translate-y-full'
        )}
        style={{
          // Force hardware acceleration with translate3d for smooth mobile performance
          transform: isOpen ? 'translate3d(0, 0, 0)' : 'translate3d(0, -100%, 0)',
          // Optimize for transform changes during animation
          willChange: isOpen ? 'transform' : 'auto',
          // Enable hardware acceleration on mobile devices
          backfaceVisibility: 'hidden',
          perspective: '1000px',
        }}
        role="navigation"
        aria-label={t('mobileMenu.mobileNavigationMenu')}
        aria-hidden={!isOpen}
        aria-modal="false"
      >
        {/* Header with close button - mobile-first one-handed optimization with landscape support */}
        <div
          className={cn(
            // Mobile-first header styling with landscape optimization
            'flex items-center justify-between px-4 py-2 xs:py-3 sm:py-3',
            'landscape:py-1.5 landscape:xs:py-2 landscape:sm:py-2.5',
            'border-b border-outline-secondary dark:border-outline-secondary-dark',
            'bg-surface dark:bg-surface-dark',
            // Progressive enhancement for larger mobile screens
            'xs:px-5 sm:px-6'
          )}
        >
          <h2
            className={cn(
              // Mobile-first text styling with landscape optimization
              'text-sm xs:text-base sm:text-base font-semibold text-content dark:text-content-dark',
              // Landscape optimization for reduced vertical space
              'landscape:text-sm landscape:xs:text-sm landscape:sm:text-base'
            )}
          >
            {t('mobileMenu.title')}
          </h2>
          <button
            ref={closeButtonRef}
            onClick={() => {
              // Prevent interactions during animation to avoid conflicts
              if (isAnimating) return;
              onClose();
            }}
            disabled={isAnimating}
            className={cn(
              // Mobile-first button styling with hardware-accelerated visual feedback, debouncing, and landscape optimization
              'p-1.5 xs:p-2 sm:p-2 landscape:p-1 landscape:xs:p-1.5 landscape:sm:p-2 rounded-xl transition-all duration-[250ms] ease-[cubic-bezier(0.2,0,0,1)]',
              'touch-manipulation transform-gpu will-change-[transform,background-color]',
              isAnimating
                ? 'opacity-75 cursor-not-allowed text-content-muted'
                : cn(
                    'text-content-muted hover:text-content dark:hover:text-content-dark',
                    'hover:bg-surface-accent dark:hover:bg-surface-accent-dark-hover',
                    'active:scale-95 active:bg-surface-accent dark:active:bg-surface-accent-dark',
                    'focus:outline-none focus:ring-2 focus:ring-primary-400/30 dark:focus:ring-primary-400/40 focus:bg-surface-accent dark:focus:bg-surface-accent-dark'
                  ),
              // Ensure minimum 44px touch targets with landscape optimization
              'min-w-10 min-h-10 xs:min-w-11 xs:min-h-11 sm:min-w-12 sm:min-h-12',
              'landscape:min-w-9 landscape:min-h-9 landscape:xs:min-w-10 landscape:xs:min-h-10 landscape:sm:min-w-11 landscape:sm:min-h-11',
              'flex items-center justify-center'
            )}
            style={{
              minHeight: '40px',
              minWidth: '40px',
              // Force hardware acceleration for smooth scaling animations
              transform: 'translate3d(0, 0, 0)',
              backfaceVisibility: 'hidden',
            }}
            aria-label={t('mobileMenu.closeNavigationMenu')}
            role="button"
            tabIndex={0}
          >
            <X
              className={cn(
                // Mobile-first icon sizing with hardware-accelerated feedback and landscape optimization
                'h-4 w-4 xs:h-5 xs:w-5 sm:h-5 sm:w-5',
                'landscape:h-4 landscape:w-4 landscape:xs:h-4 landscape:xs:w-4 landscape:sm:h-5 landscape:sm:w-5',
                'transition-all duration-[250ms] ease-[cubic-bezier(0.2,0,0,1)]',
                'hover:scale-105 active:scale-95 transform-gpu will-change-[transform]'
              )}
              style={{
                // Force hardware acceleration for smooth icon scaling
                transform: 'translate3d(0, 0, 0)',
                backfaceVisibility: 'hidden',
              }}
            />
          </button>
        </div>

        {/* Scrollable content area - mobile-first spacing with optimized scroll behavior */}
        <div
          className="flex-1 overflow-y-auto bg-surface dark:bg-surface-dark overscroll-contain"
          style={{
            // Ensure smooth scrolling on mobile devices
            WebkitOverflowScrolling: 'touch',
            // Prevent scroll chaining to parent elements
            overscrollBehavior: 'contain',
          }}
        >
          <nav
            className={cn(
              // Mobile-first padding with landscape optimization
              'px-4 py-3 xs:py-4 sm:py-4',
              'landscape:py-2 landscape:xs:py-3 landscape:sm:py-3',
              // Progressive enhancement for larger screens
              'xs:px-5 sm:px-6'
            )}
            role="menu"
            aria-label={t('mobileMenu.mainNavigationAria')}
          >
            {/* Mobile-first navigation with optimal spacing and landscape optimization */}
            <div
              className={cn(
                // Mobile-first spacing between items - ensuring minimum 8px with landscape optimization
                'space-y-1.5 xs:space-y-2 sm:space-y-2',
                // Landscape optimization for reduced vertical space
                'landscape:space-y-1 landscape:xs:space-y-1.5 landscape:sm:space-y-2'
              )}
              role="group"
              aria-label={t('mobileMenu.navigationLinksAria')}
            >
              {navigation.map(item => {
                const itemName = item.translationKey ? t(item.translationKey) : item.name;
                const isAnchor = item.href.startsWith('#');
                const isActive = location.pathname === item.href;

                if (isAnchor) {
                  return (
                    <button
                      key={item.name}
                      onClick={() => {
                        // Prevent interactions during animation to avoid conflicts
                        if (isAnimating) return;

                        handleAnchorClick(item.href, navigate, location.pathname);
                        onClose();
                      }}
                      disabled={isAnimating}
                      className={cn(
                        // Mobile-first button styling with hardware-accelerated visual feedback and landscape optimization
                        'group w-full flex items-center justify-between px-3 py-2.5 xs:px-4 xs:py-3 sm:px-4 sm:py-3 rounded-xl',
                        'landscape:px-3 landscape:py-2 landscape:xs:px-4 landscape:xs:py-2.5 landscape:sm:px-4 landscape:sm:py-3',
                        'text-left transition-all duration-[250ms] ease-[cubic-bezier(0.2,0,0,1)] touch-manipulation',
                        'text-content-secondary dark:text-content-secondary-dark',
                        'hover:text-content dark:hover:text-content-dark',
                        'hover:bg-surface-accent dark:hover:bg-surface-accent-dark-hover',
                        'hover:shadow-sm hover:translate-x-1',
                        'active:scale-[0.98] active:bg-surface-accent dark:active:bg-surface-accent-dark active:translate-x-0',
                        'focus:outline-none focus:ring-2 focus:ring-primary-400/30 dark:focus:ring-primary-400/40 focus:bg-surface-accent dark:focus:bg-surface-accent-dark',
                        'transform-gpu will-change-[transform,background-color]',
                        // Ensure minimum 44px touch targets with landscape optimization
                        'min-h-10 xs:min-h-11 sm:min-h-12 text-sm font-medium',
                        'landscape:min-h-9 landscape:xs:min-h-10 landscape:sm:min-h-11'
                      )}
                      style={{
                        minHeight: '40px',
                        // Force hardware acceleration for smooth transform animations
                        transform: 'translate3d(0, 0, 0)',
                        backfaceVisibility: 'hidden',
                      }}
                      aria-label={t('navigation.navigateToSectionAria', { item: itemName })}
                      role="menuitem"
                      tabIndex={0}
                    >
                      <span
                        className={cn(
                          // Mobile-first text styling with landscape optimization
                          'text-sm xs:text-base sm:text-sm font-medium',
                          // Landscape optimization for better readability in reduced space
                          'landscape:text-sm landscape:xs:text-sm landscape:sm:text-sm'
                        )}
                      >
                        {itemName}
                      </span>
                      <ChevronRight
                        className={cn(
                          // Mobile-first icon sizing with hardware-accelerated feedback and landscape optimization
                          'h-4 w-4 xs:h-5 xs:w-5 sm:h-4 sm:w-4',
                          'landscape:h-3.5 landscape:w-3.5 landscape:xs:h-4 landscape:xs:w-4 landscape:sm:h-4 landscape:sm:w-4',
                          'text-content-muted opacity-60 transition-all duration-[250ms] ease-[cubic-bezier(0.2,0,0,1)]',
                          'group-hover:opacity-100 group-hover:translate-x-1 group-hover:text-content dark:group-hover:text-content-dark',
                          'transform-gpu will-change-[transform,opacity]'
                        )}
                        style={{
                          // Force hardware acceleration for smooth icon animations
                          transform: 'translate3d(0, 0, 0)',
                          backfaceVisibility: 'hidden',
                        }}
                      />
                    </button>
                  );
                }

                return (
                  <Link
                    key={item.name}
                    to={item.href}
                    onClick={e => {
                      // Prevent interactions during animation to avoid conflicts
                      if (isAnimating) {
                        e.preventDefault();
                        return;
                      }
                      onClose();
                    }}
                    className={cn(
                      // Mobile-first link styling with hardware-accelerated visual feedback and landscape optimization
                      'group flex items-center justify-between px-3 py-2.5 xs:px-4 xs:py-3 sm:px-4 sm:py-3 rounded-xl',
                      'landscape:px-3 landscape:py-2 landscape:xs:px-4 landscape:xs:py-2.5 landscape:sm:px-4 landscape:sm:py-3',
                      'transition-all duration-[250ms] ease-[cubic-bezier(0.2,0,0,1)] touch-manipulation',
                      'active:scale-[0.98]',
                      'focus:outline-none focus:ring-2 focus:ring-primary-400/30 dark:focus:ring-primary-400/40',
                      'transform-gpu will-change-[transform,background-color]',
                      // Ensure minimum 44px touch targets with landscape optimization
                      'min-h-10 xs:min-h-11 sm:min-h-12 text-sm font-medium',
                      'landscape:min-h-9 landscape:xs:min-h-10 landscape:sm:min-h-11',
                      isActive
                        ? cn(
                            'text-content dark:text-content-dark',
                            'bg-surface-accent dark:bg-surface-accent-dark',
                            'border border-primary-500/20 dark:border-primary-500/20',
                            'shadow-sm'
                          )
                        : cn(
                            'text-content-secondary dark:text-content-secondary-dark',
                            'hover:text-content dark:hover:text-content-dark',
                            'hover:bg-surface-accent dark:hover:bg-surface-accent-dark-hover',
                            'hover:shadow-sm hover:translate-x-1',
                            'active:bg-surface-accent dark:active:bg-surface-accent-dark active:translate-x-0',
                            'focus:bg-surface-accent dark:focus:bg-surface-accent-dark'
                          )
                    )}
                    style={{
                      minHeight: '40px',
                      // Force hardware acceleration for smooth link animations
                      transform: 'translate3d(0, 0, 0)',
                      backfaceVisibility: 'hidden',
                    }}
                    aria-label={t('navigation.navigateToPageAria', { item: itemName })}
                    aria-current={isActive ? 'page' : undefined}
                    role="menuitem"
                    tabIndex={0}
                  >
                    <span
                      className={cn(
                        // Mobile-first text styling with landscape optimization
                        'text-sm xs:text-base sm:text-sm font-medium',
                        // Landscape optimization for better readability in reduced space
                        'landscape:text-sm landscape:xs:text-sm landscape:sm:text-sm'
                      )}
                    >
                      {itemName}
                    </span>
                    <ChevronRight
                      className={cn(
                        // Mobile-first icon styling with hardware-accelerated feedback and landscape optimization
                        'h-4 w-4 xs:h-5 xs:w-5 sm:h-4 sm:w-4',
                        'landscape:h-3.5 landscape:w-3.5 landscape:xs:h-4 landscape:xs:w-4 landscape:sm:h-4 landscape:sm:w-4',
                        'transition-all duration-[250ms] ease-[cubic-bezier(0.2,0,0,1)]',
                        'group-hover:translate-x-1 transform-gpu will-change-[transform,opacity]',
                        isActive
                          ? 'text-primary-500 opacity-100'
                          : 'text-content-muted opacity-60 group-hover:opacity-100 group-hover:text-content dark:group-hover:text-content-dark'
                      )}
                      style={{
                        // Force hardware acceleration for smooth icon animations
                        transform: 'translate3d(0, 0, 0)',
                        backfaceVisibility: 'hidden',
                      }}
                    />
                  </Link>
                );
              })}
            </div>
          </nav>
        </div>

        {/* Bottom auth section - mobile-first sticky positioning for thumb access with landscape optimization */}
        {!isAuthenticated && (
          <div
            className={cn(
              // Mobile-first sticky bottom positioning with landscape optimization
              'sticky bottom-0 p-3 xs:p-4 sm:p-4 border-t border-outline-secondary dark:border-outline-secondary-dark',
              'landscape:p-2 landscape:xs:p-3 landscape:sm:p-3',
              'bg-surface dark:bg-surface-dark',
              // Progressive enhancement for larger screens
              'xs:p-5 sm:p-4'
            )}
            role="group"
            aria-label={t('mobileMenu.authenticationActionsAria')}
          >
            <div
              className={cn(
                // Mobile-first spacing with minimum 8px between buttons and landscape optimization
                'space-y-2.5 xs:space-y-3 sm:space-y-3',
                // Landscape optimization for reduced vertical space
                'landscape:space-y-2 landscape:xs:space-y-2.5 landscape:sm:space-y-3'
              )}
            >
              <Link
                to={ROUTES.LOGIN}
                onClick={e => {
                  // Prevent interactions during animation to avoid conflicts
                  if (isAnimating) {
                    e.preventDefault();
                    return;
                  }
                  onClose();
                }}
                className={cn(
                  // Mobile-first button styling with hardware-accelerated visual feedback and landscape optimization
                  'flex items-center justify-center px-4 py-2.5 xs:py-3 sm:py-3 rounded-xl',
                  'landscape:py-2 landscape:xs:py-2.5 landscape:sm:py-3',
                  'text-content dark:text-content-dark font-medium text-sm xs:text-base sm:text-sm',
                  'landscape:text-sm landscape:xs:text-sm landscape:sm:text-sm',
                  'border border-outline-secondary dark:border-outline-secondary-dark',
                  'hover:bg-surface-accent dark:hover:bg-surface-accent-dark-hover',
                  'hover:border-outline dark:hover:border-outline-dark',
                  'hover:shadow-sm hover:scale-[1.02]',
                  'transition-all duration-[250ms] ease-[cubic-bezier(0.2,0,0,1)] touch-manipulation',
                  'active:scale-[0.98] active:bg-surface-accent dark:active:bg-surface-accent-dark',
                  'focus:outline-none focus:ring-2 focus:ring-primary-400/30 dark:focus:ring-primary-400/40 focus:bg-surface-accent dark:focus:bg-surface-accent-dark',
                  'transform-gpu will-change-[transform,background-color,border-color]',
                  // Ensure minimum 44px touch targets with landscape optimization
                  'min-h-10 xs:min-h-11 sm:min-h-12',
                  'landscape:min-h-9 landscape:xs:min-h-10 landscape:sm:min-h-11'
                )}
                style={{
                  minHeight: '40px',
                  // Force hardware acceleration for smooth button animations
                  transform: 'translate3d(0, 0, 0)',
                  backfaceVisibility: 'hidden',
                }}
                aria-label={t('mobileMenu.signInAria')}
                role="menuitem"
                tabIndex={0}
              >
                {t('mobileMenu.signIn')}
              </Link>
            </div>
          </div>
        )}
      </div>
    </>
  );
};
