import { useCallback, memo } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { useFrontendLanguage } from 'app/providers/FrontendLanguageContext';
import { NAVIGATION } from 'shared/config';
import { cn } from 'shared/lib/utils';

const scrollToSection = (href: string) => {
  const element = document.querySelector(href);
  element?.scrollIntoView({ behavior: 'smooth' });
};

const NavigationComponent = () => {
  const location = useLocation();
  const navigate = useNavigate();
  const { t } = useFrontendLanguage();

  // Optimize anchor link handling with useCallback
  const handleAnchorClick = useCallback(
    (href: string) => {
      if (location.pathname === '/') {
        scrollToSection(href);
      } else {
        navigate('/' + href);
      }
    },
    [location.pathname, navigate]
  );

  return (
    <nav
      className="flex items-center gap-2 md:gap-4 lg:gap-6"
      role="navigation"
      aria-label={t('navigation.mainAriaLabel')}
    >
      {NAVIGATION.map(item => {
        const itemName = item.translationKey ? t(item.translationKey) : item.name;
        const isAnchor = item.href.startsWith('#');
        const isActive = location.pathname === item.href;

        if (isAnchor) {
          return (
            <button
              key={item.name}
              onClick={() => handleAnchorClick(item.href)}
              className={cn(
                // Mobile-first base styles with clean text-only styling
                'text-xs font-medium transition-colors duration-200',
                'text-content-secondary dark:text-content-secondary-dark',
                // Progressive enhancement for larger screens
                'lg:text-sm',
                // Hover states - clean text-only
                'hover:text-content dark:hover:text-content-dark',
                // Enhanced focus states for accessibility
                'focus:outline-none focus:text-content dark:focus:text-content-dark',
                // Adequate padding for touch targets
                'px-3 py-2 touch-manipulation',
                // Ensure minimum touch target size for accessibility
                'min-h-11'
              )}
              style={{
                minHeight: '44px',
              }}
              aria-label={t('navigation.navigateToSectionAria', { item: itemName })}
              role="button"
            >
              {itemName}
            </button>
          );
        }

        return (
          <Link
            key={item.name}
            to={item.href}
            className={cn(
              // Mobile-first base styles with clean text-only styling
              'text-xs font-medium transition-colors duration-200',
              'px-3 py-2 touch-manipulation',
              // Ensure minimum touch target size
              'min-h-11 flex items-center',
              // Progressive enhancement for larger screens
              'lg:text-sm',
              // Enhanced focus states for accessibility
              'focus:outline-none',
              // Conditional styles based on active state - clean text-only
              isActive
                ? 'text-content dark:text-content-dark'
                : [
                    'text-content-secondary dark:text-content-secondary-dark',
                    'hover:text-content dark:hover:text-content-dark',
                    'focus:text-content dark:focus:text-content-dark',
                  ]
            )}
            style={{
              minHeight: '44px',
            }}
            aria-current={isActive ? 'page' : undefined}
            aria-label={t('navigation.navigateToPageAria', { item: itemName })}
          >
            {itemName}
          </Link>
        );
      })}
    </nav>
  );
};

// Memoize the Navigation component to prevent unnecessary re-renders
export const Navigation = memo(NavigationComponent);
