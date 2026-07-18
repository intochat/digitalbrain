import { Link } from 'react-router-dom';
import { useFrontendLanguage } from 'app/providers';
import { ROUTES } from 'shared/config/routes';
import { cn } from 'shared/lib/utils';
import { useAuthStore } from 'shared/store/auth';

export const UserActions = () => {
  const { t } = useFrontendLanguage();
  const { isAuthenticated, user } = useAuthStore();
  const displayedUserName = user?.username ? user.username.toLowerCase() : (user?.name ?? 'user');

  if (!isAuthenticated) {
    return (
      <div className="flex items-center gap-2 xs:gap-3 md:gap-4">
        <Link
          to={ROUTES.LOGIN}
          className={cn(
            'px-3 py-2 text-sm font-medium transition-colors duration-150',
            'text-content-secondary dark:text-content-secondary-dark',
            'hover:text-content dark:hover:text-content-dark',
            'focus:outline-none focus:text-content dark:focus:text-content-dark',
            'touch-manipulation',
            'min-h-11 flex items-center justify-center',
            'hidden xs:flex',
          )}
          aria-label={t('Sign in to your account')}
        >
          {t('Sign in')}
        </Link>
      </div>
    );
  }

  return (
    <div className="flex items-center">
      <Link
        to={ROUTES.PROFILE}
        className={cn(
          'group flex items-center gap-2 p-2 transition-colors duration-150',
          'hover:opacity-90',
          'focus:outline-none focus:opacity-90',
          'touch-manipulation',
          'min-h-11 min-w-11',
        )}
        aria-label={t("Go to {displayedUserName}'s profile", { displayedUserName })}
      >
        <img
          src={user?.avatar}
          alt={t("{displayedUserName}'s profile picture", { displayedUserName })}
          className="h-8 w-8 rounded-full object-cover md:h-7 md:w-7"
          role="img"
        />
        <span
          className={cn(
            'hidden sm:block text-sm font-medium',
            'text-content dark:text-content-dark',
            'transition-colors duration-150',
            'group-hover:text-content dark:group-hover:text-content-dark',
          )}
        >
          {displayedUserName}
        </span>
      </Link>
    </div>
  );
};
