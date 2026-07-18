import { Link } from 'react-router-dom';
import { useFrontendLanguage } from 'app/providers';
import { ROUTES } from 'shared/config/routes';

export const Logo = () => {
  const { t } = useFrontendLanguage();

  return (
    <Link
      to={ROUTES.HOME}
      aria-label={t('Go to TripRadar home page')}
      className="group flex min-h-11 items-center py-1 touch-manipulation"
    >
      <span
        className="text-lg font-semibold tracking-tight text-content dark:text-content-dark sm:text-xl transition-opacity duration-150 group-hover:opacity-80 group-active:opacity-70"
        style={{ letterSpacing: '-0.03em' }}
      >
        TripRadar
      </span>
    </Link>
  );
};
