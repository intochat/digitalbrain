import { memo, useEffect } from 'react';
import { Menu, X } from 'lucide-react';
import { useFrontendLanguage } from 'app/providers/FrontendLanguageContext';
import { useScrollDetection } from 'shared/lib/hooks';
import { ThemeToggle } from 'shared/ui';
import { LanguageToggle } from 'shared/ui';
import { useMobileMenuState } from '../lib';
import { Logo } from './Logo';
import { MobileMenu } from './MobileMenu';
import { Navigation } from './Navigation';
import { UserActions } from './UserActions';

const HeaderComponent = () => {
  const { sentinelRef, isScrolled } = useScrollDetection();
  const { isMenuOpen, isAnimating, handleMenuToggle, handleMenuClose, cleanup } = useMobileMenuState();
  const { t } = useFrontendLanguage();

  // Cleanup timeouts on component unmount
  useEffect(() => {
    return cleanup;
  }, [cleanup]);

  return (
    <>
      <div ref={sentinelRef} className="h-1" />
      <header
        className={`fixed top-0 left-0 right-0 z-50 transition-all duration-[250ms] ease-[cubic-bezier(0.2,0,0,1)] transform-gpu will-change-[background-color,backdrop-filter,box-shadow] ${
          isScrolled
            ? 'bg-surface/85 dark:bg-surface-dark/85 backdrop-blur-md shadow-sm'
            : 'bg-surface/70 dark:bg-surface-dark/70 backdrop-blur-sm'
        }`}
        style={{
          // Force hardware acceleration for smooth header transitions
          transform: 'translate3d(0, 0, 0)',
          backfaceVisibility: 'hidden',
          // Optimize for background and backdrop filter changes
          willChange: isScrolled ? 'background-color, backdrop-filter, box-shadow' : 'auto',
        }}
      >
        {/* Mobile-first container with progressive enhancement and landscape optimization */}
        <div className="px-4 xs:px-5 sm:px-6 lg:px-8 max-w-7xl mx-auto" id="header-content">
          {/* Mobile-first layout: Logo + Actions with responsive height adjustments for landscape */}
          <div className="flex justify-between items-center h-16 xs:h-20 sm:h-24 landscape:h-14 landscape:xs:h-16 landscape:sm:h-20">
            {/* Logo - consistent across all breakpoints */}
            <Logo />

            {/* Navigation - hidden on mobile, visible on desktop (mobile-first approach) */}
            <div className="hidden md:block">
              <Navigation />
            </div>

            {/* Actions container - mobile-first with progressive enhancement and landscape-optimized spacing */}
            <div className="flex items-center gap-1 xs:gap-2 sm:gap-3 md:gap-4 landscape:gap-1 landscape:xs:gap-2 landscape:sm:gap-3">
              {/* User actions - different layouts for mobile vs desktop */}
              <div className="md:hidden">
                {/* Mobile: Compact user actions with proper touch targets */}
                <UserActions />
              </div>

              <div className="hidden md:flex items-center gap-4">
                {/* Desktop: Full user actions without separator */}
                <UserActions />
              </div>

              {/* Language and theme toggles - always visible */}
              <div className="flex items-center">
                <LanguageToggle />
                <ThemeToggle />
              </div>

              {/* Mobile menu button - mobile-first with enhanced visual feedback, debouncing, and landscape optimization */}
              <button
                onClick={handleMenuToggle}
                disabled={isAnimating}
                className={`p-1.5 xs:p-2 sm:p-2 landscape:p-1 landscape:xs:p-1.5 landscape:sm:p-2 rounded-xl transition-all duration-[250ms] ease-[cubic-bezier(0.2,0,0,1)] md:hidden min-w-10 min-h-10 xs:min-w-11 xs:min-h-11 sm:min-w-11 sm:min-h-11 landscape:min-w-9 landscape:min-h-9 landscape:xs:min-w-10 landscape:xs:min-h-10 landscape:sm:min-w-11 landscape:sm:min-h-11 flex items-center justify-center touch-manipulation focus:outline-none focus:ring-2 focus:ring-primary-400/30 dark:focus:ring-primary-400/40 focus:bg-surface-accent dark:focus:bg-surface-accent-dark outline-none border-none transform-gpu will-change-[transform,background-color] ${
                  isAnimating
                    ? 'opacity-75 cursor-not-allowed bg-surface-accent dark:bg-surface-accent-dark text-content-muted dark:text-muted-dark'
                    : isMenuOpen
                      ? 'bg-surface-accent dark:bg-surface-accent-dark text-content dark:text-content-dark'
                      : 'hover:bg-surface-accent dark:hover:bg-surface-accent-dark-hover text-content-secondary dark:text-content-secondary-dark hover:text-content dark:hover:text-content-dark'
                } ${!isAnimating ? 'active:scale-95 active:bg-surface-accent dark:active:bg-surface-accent-dark' : ''}`}
                aria-label={isMenuOpen ? t('header.closeNavigationMenu') : t('header.openNavigationMenu')}
                aria-expanded={isMenuOpen}
                aria-controls="mobile-menu"
                aria-haspopup="true"
                aria-busy={isAnimating}
                role="button"
                tabIndex={isAnimating ? -1 : 0}
                style={{
                  minWidth: '40px',
                  minHeight: '40px',
                  // Force hardware acceleration for smooth burger menu button animations
                  transform: 'translate3d(0, 0, 0)',
                  backfaceVisibility: 'hidden',
                }}
              >
                {isMenuOpen ? (
                  <X
                    className={`h-4 w-4 xs:h-5 xs:w-5 sm:h-5 sm:w-5 landscape:h-4 landscape:w-4 landscape:xs:h-4 landscape:xs:w-4 landscape:sm:h-5 landscape:sm:w-5 transition-all duration-[250ms] ease-[cubic-bezier(0.2,0,0,1)] rotate-0 scale-100 transform-gpu will-change-[transform] ${
                      isAnimating ? 'animate-pulse' : ''
                    }`}
                    style={{
                      // Force hardware acceleration for smooth icon transitions
                      transform: 'translate3d(0, 0, 0)',
                      backfaceVisibility: 'hidden',
                    }}
                  />
                ) : (
                  <Menu
                    className={`h-4 w-4 xs:h-5 xs:w-5 sm:h-5 sm:w-5 landscape:h-4 landscape:w-4 landscape:xs:h-4 landscape:xs:w-4 landscape:sm:h-5 landscape:sm:w-5 transition-all duration-[250ms] ease-[cubic-bezier(0.2,0,0,1)] rotate-0 scale-100 transform-gpu will-change-[transform] ${
                      isAnimating ? 'animate-pulse' : 'hover:scale-105'
                    }`}
                    style={{
                      // Force hardware acceleration for smooth icon transitions
                      transform: 'translate3d(0, 0, 0)',
                      backfaceVisibility: 'hidden',
                    }}
                  />
                )}
              </button>
            </div>
          </div>
        </div>

        {/* Mobile menu - positioned outside header-content to avoid blur effects */}
        <MobileMenu isOpen={isMenuOpen} isAnimating={isAnimating} onClose={handleMenuClose} />
      </header>
    </>
  );
};

// Memoize the Header component to prevent unnecessary re-renders
export const Header = memo(HeaderComponent);
