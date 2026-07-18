import { ReactNode, useEffect } from 'react';
import { ArrowLeft, BarChart3, Clock3, CreditCard, Lock, LogOut, Map, Settings, User } from 'lucide-react';
import { useLocation } from 'react-router-dom';
import { useFrontendLanguage } from 'app/providers/FrontendLanguageContext';
import { useSubscriptionQuery } from 'entities/payment/api';
import { useLogout } from 'features/auth';
import { getTelegramBotUsername } from 'shared/config/env';
import type { FrontendTranslationKey } from 'shared/i18n';
import { useNavigationPersistence } from 'shared/lib/hooks';
import { UnsavedChangesDialog } from 'shared/ui';

interface ProfileLayoutProps {
  children: ReactNode;
  hasUnsavedChanges?: boolean;
  onNavigationAttempt?: (targetPath: string) => boolean | Promise<boolean>;
  maxWidthClassName?: string;
}

interface NavigationItem {
  id: string;
  labelKey: FrontendTranslationKey;
  icon: typeof User;
  path: string;
}

const navigationItems: NavigationItem[] = [
  { id: 'profile', labelKey: 'profile.layout.profile', icon: User, path: '/profile' },
  { id: 'security', labelKey: 'profile.layout.security', icon: Lock, path: '/profile/security' },
  { id: 'billing', labelKey: 'profile.layout.billing', icon: CreditCard, path: '/profile/billing' },
  { id: 'usage', labelKey: 'profile.layout.usage', icon: BarChart3, path: '/profile/usage' },
  { id: 'preferences', labelKey: 'profile.layout.preferences', icon: Settings, path: '/profile/preferences' },
  {
    id: 'scheduled-requests',
    labelKey: 'profile.layout.scheduledRequests',
    icon: Clock3,
    path: '/profile/scheduled-requests',
  },
  { id: 'trips', labelKey: 'profile.layout.trips', icon: Map, path: '/profile/trips' },
];

const PAGE_KEY_MAP: Record<string, string> = {
  '/profile': 'profile',
  '/security': 'security',
  '/billing': 'billing',
  '/usage': 'usage',
  '/preferences': 'preferences',
  '/scheduled-requests': 'scheduled-requests',
  '/trips': 'trips',
};

const getPageKey = (path: string) => {
  const segment = Object.keys(PAGE_KEY_MAP).find(key => (key === '/profile' ? path === key : path.includes(key)));
  return segment ? PAGE_KEY_MAP[segment] : 'profile';
};

export const ProfileLayout = ({
  children,
  hasUnsavedChanges = false,
  onNavigationAttempt,
  maxWidthClassName = 'max-w-7xl',
}: ProfileLayoutProps) => {
  const location = useLocation();
  const logout = useLogout();
  const { t } = useFrontendLanguage();
  const subscriptionQuery = useSubscriptionQuery();
  const isPro =
    !subscriptionQuery.isLoading &&
    !subscriptionQuery.isError &&
    Boolean(subscriptionQuery.data) &&
    subscriptionQuery.data?.tierType?.toLowerCase() !== 'basic';

  const currentPath = location.pathname;
  const localizedNavigationItems = navigationItems.map(item => ({
    ...item,
    label: t(item.labelKey),
  }));

  const { safeNavigate, confirmNavigation, cancelNavigation, pendingNavigation, setHasUnsavedChanges } =
    useNavigationPersistence({
      pageKey: getPageKey(currentPath),
      enableWarning: true,
      onBeforeNavigate: onNavigationAttempt,
    });

  useEffect(() => {
    setHasUnsavedChanges(hasUnsavedChanges);
  }, [hasUnsavedChanges, setHasUnsavedChanges]);

  const handleNavigation = (path: string) => safeNavigate(path);
  const handleBack = () => safeNavigate('/profile');

  const isRouteActive = (itemPath: string): boolean =>
    itemPath === '/profile' ? currentPath === '/profile' : currentPath.startsWith(itemPath);

  const getSectionTitle = () => {
    const currentItem = localizedNavigationItems.find(item => isRouteActive(item.path));
    return currentItem?.label || t('profile.layout.profile');
  };

  const getPaidTooltip = (id: string) =>
    id === 'trips'
      ? t('Trip vaults and history are available only for paid users')
      : t('Scheduled Requests Only for Paid users');

  return (
    <div className="relative flex-1 bg-surface dark:bg-surface-dark">
      <div className={`relative w-full ${maxWidthClassName} mx-auto p-4 sm:p-6 md:p-8 pt-14 sm:pt-16 md:pt-12`}>
        {/* Mobile Header */}
        {currentPath !== '/profile' && (
          <div className="lg:hidden mb-4 sm:mb-6 pt-4">
            <div className="flex items-center gap-3 mb-3">
              <button
                onClick={handleBack}
                className="p-2.5 text-content-secondary dark:text-content-secondary-dark hover:text-content dark:hover:text-content-dark rounded-lg transition-colors touch-manipulation"
                aria-label={t('profile.layout.goBackToProfile')}
              >
                <ArrowLeft className="h-4.5 w-4.5" />
              </button>
              <h1 className="text-lg font-semibold text-content dark:text-content-dark truncate">
                {getSectionTitle()}
              </h1>
            </div>
          </div>
        )}

        {/* Mobile Navigation Grid */}
        {currentPath === '/profile' && (
          <div className="lg:hidden mb-4 sm:mb-6 pt-4">
            <div className="grid grid-cols-2 gap-2 sm:gap-3">
              {navigationItems.slice(1).map(item => {
                const isPaidFeature = item.id === 'trips' || item.id === 'scheduled-requests';
                const isDisabled = isPaidFeature && !isPro;

                return (
                  <div key={item.id} className="relative group">
                    <button
                      onClick={() => !isDisabled && handleNavigation(item.path)}
                      disabled={isDisabled}
                      className={`w-full flex flex-col items-center gap-2 p-4 rounded-lg border border-outline dark:border-outline-dark transition-colors touch-manipulation ${
                        isDisabled
                          ? 'opacity-40 cursor-not-allowed'
                          : 'hover:bg-surface-accent dark:hover:bg-surface-accent-dark'
                      }`}
                    >
                      <item.icon className="h-5 w-5 text-content-muted dark:text-content-muted-dark" />
                      <span className="text-xs font-medium text-content dark:text-content-dark text-center">
                        {t(item.labelKey)}
                      </span>
                    </button>
                    {isDisabled && (
                      <div className="absolute bottom-full left-1/2 mb-1.5 -translate-x-1/2 px-2.5 py-1.5 bg-content dark:bg-content-dark text-surface dark:text-surface-dark text-xs rounded-md opacity-0 invisible group-hover:opacity-100 group-hover:visible transition-opacity whitespace-nowrap z-50 pointer-events-none">
                        {getPaidTooltip(item.id)}
                      </div>
                    )}
                  </div>
                );
              })}
            </div>
          </div>
        )}

        <div className="flex flex-col lg:flex-row gap-6 lg:gap-8 pt-4 sm:pt-6 lg:pt-8 lg:items-start">
          {/* Desktop Sidebar */}
          <div className="hidden lg:block w-64 xl:w-72 flex-shrink-0">
            <div className="sticky top-0">
              <nav className="space-y-0.5">
                {localizedNavigationItems.map(item => {
                  const isPaidFeature = item.id === 'trips' || item.id === 'scheduled-requests';
                  const isDisabled = isPaidFeature && !isPro;
                  const active = isRouteActive(item.path);

                  return (
                    <div key={item.id} className="relative group">
                      <button
                        onClick={() => !isDisabled && handleNavigation(item.path)}
                        disabled={isDisabled}
                        className={`w-full flex items-center gap-2.5 px-3 py-2 text-[13px] font-medium rounded-lg transition-colors ${
                          active
                            ? 'bg-surface-accent dark:bg-surface-accent-dark text-content dark:text-content-dark'
                            : 'text-content-secondary dark:text-content-secondary-dark hover:bg-surface-accent dark:hover:bg-surface-accent-dark hover:text-content dark:hover:text-content-dark'
                        } ${isDisabled ? 'opacity-40 cursor-not-allowed' : ''}`}
                      >
                        <item.icon
                          className={`h-4 w-4 ${active ? 'text-content dark:text-content-dark' : 'text-content-muted dark:text-content-muted-dark'}`}
                        />
                        <span className="truncate">{item.label}</span>
                      </button>
                      {isDisabled && (
                        <div className="absolute left-full top-1/2 -translate-y-1/2 ml-2 px-2.5 py-1.5 bg-content dark:bg-content-dark text-surface dark:text-surface-dark text-xs rounded-md opacity-0 invisible group-hover:opacity-100 group-hover:visible transition-opacity whitespace-nowrap z-50 pointer-events-none">
                          {getPaidTooltip(item.id)}
                        </div>
                      )}
                    </div>
                  );
                })}
              </nav>

              <div className="mt-5 pt-4 border-t border-outline/40 dark:border-outline-dark/40">
                <a
                  href={`https://t.me/${getTelegramBotUsername()}`}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="flex items-center gap-2.5 px-3 py-2.5 rounded-lg transition-colors hover:bg-surface-accent dark:hover:bg-surface-accent-dark group"
                >
                  <svg viewBox="0 0 240 240" className="h-4 w-4 flex-shrink-0">
                    <circle cx="120" cy="120" r="120" fill="#2AABEE" />
                    <path d="M98 175c-3.888 0-3.227-1.468-4.568-5.17L82 132.207 170 80" fill="#C8DAEA" />
                    <path d="M98 175c3 0 4.325-1.372 6-3l16-15.558-19.958-12.035" fill="#A9C9DD" />
                    <path
                      d="M100.04 144.41l48.36 35.729c5.519 3.045 9.501 1.468 10.876-5.123l19.685-92.763c2.015-8.08-3.08-11.746-8.36-9.349l-115.59 44.571c-7.89 3.165-7.843 7.567-1.438 9.528l29.663 9.259 68.673-43.325c3.242-1.966 6.218-.91 3.776 1.258"
                      fill="#FFFFFF"
                    />
                  </svg>
                  <span className="text-[13px] font-medium text-content-secondary dark:text-content-secondary-dark group-hover:text-content dark:group-hover:text-content-dark truncate">
                    {t('Plan trips in Telegram')}
                  </span>
                </a>
              </div>

              <div className="mt-3 pt-3 border-t border-outline/40 dark:border-outline-dark/40">
                <button
                  onClick={logout}
                  className="w-full flex items-center gap-2.5 px-3 py-2 text-[13px] font-medium text-content-secondary dark:text-content-secondary-dark hover:text-red-600 dark:hover:text-red-400 hover:bg-surface-accent dark:hover:bg-surface-accent-dark rounded-lg transition-colors touch-manipulation"
                >
                  <LogOut className="h-4 w-4" />
                  <span>{t('profile.layout.signOut')}</span>
                </button>
              </div>
            </div>
          </div>

          {/* Main Content */}
          <div className="flex-1 min-w-0">{children}</div>
        </div>

        {/* Mobile Sign Out */}
        {currentPath === '/profile' && (
          <div className="lg:hidden mt-4 sm:mt-6">
            <button
              onClick={logout}
              className="w-full flex items-center justify-center gap-2 py-3 text-sm font-medium text-red-600 dark:text-red-400 hover:bg-surface-accent dark:hover:bg-surface-accent-dark rounded-lg transition-colors touch-manipulation"
            >
              <LogOut className="h-4 w-4" />
              <span>{t('profile.layout.signOut')}</span>
            </button>
          </div>
        )}
      </div>

      <UnsavedChangesDialog
        isOpen={!!pendingNavigation}
        targetPath={pendingNavigation}
        onConfirm={confirmNavigation}
        onCancel={cancelNavigation}
      />
    </div>
  );
};
