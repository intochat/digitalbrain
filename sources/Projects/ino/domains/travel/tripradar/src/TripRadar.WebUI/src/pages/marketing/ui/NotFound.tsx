import { Link } from 'react-router-dom';
import { useFrontendLanguage } from 'app/providers';
import { ROUTES } from 'shared/config/routes';

export const NotFound = () => {
  const { t } = useFrontendLanguage();

  return (
    <div className="min-h-[calc(100vh-220px)] bg-surface dark:bg-surface-dark px-4 py-12 sm:py-20">
      <div className="mx-auto max-w-2xl rounded-lg border border-outline dark:border-outline-dark bg-surface dark:bg-surface-dark p-6 sm:p-10">
        <p className="text-sm font-semibold uppercase tracking-wide text-content-secondary dark:text-content-secondary-dark">
          {t('Error 404')}
        </p>
        <h1 className="mt-3 text-3xl sm:text-4xl font-semibold text-content dark:text-content-dark">
          {t('Page not found')}
        </h1>
        <p className="mt-4 text-sm sm:text-base text-content-secondary dark:text-content-secondary-dark">
          {t('The page you are looking for does not exist or has been moved.')}
        </p>
        <div className="mt-8 flex gap-3">
          <Link
            to={ROUTES.HOME}
            className="inline-flex items-center justify-center rounded-xl bg-button dark:bg-button-dark px-5 py-3 text-sm font-medium text-button-text dark:text-button-text-dark hover:bg-button-hover dark:hover:bg-button-hover-dark"
          >
            {t('Go to home')}
          </Link>
          <Link
            to={ROUTES.PRICING}
            className="inline-flex items-center justify-center rounded-xl border border-outline dark:border-outline-dark px-5 py-3 text-sm font-medium text-content dark:text-content-dark hover:bg-surface dark:hover:bg-surface-dark"
          >
            {t('View pricing')}
          </Link>
        </div>
      </div>
    </div>
  );
};
