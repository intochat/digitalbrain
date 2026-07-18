import { useEffect, useRef } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { useFrontendLanguage } from 'app/providers';
import { useUnsubscribeMarketingMutation } from 'entities/user/api';
import { ROUTES } from 'shared/config/routes';

export const Unsubscribe = () => {
  const { t } = useFrontendLanguage();
  const [searchParams] = useSearchParams();
  const unsubscribeMutation = useUnsubscribeMarketingMutation();
  const hasStartedRef = useRef(false);
  const username = searchParams.get('username')?.trim() ?? '';
  const email = searchParams.get('email')?.trim() ?? '';
  const hasTarget = Boolean(username || email);

  useEffect(() => {
    if (!hasTarget || hasStartedRef.current) {
      return;
    }

    hasStartedRef.current = true;
    unsubscribeMutation.mutate({ username: username || undefined, email: email || undefined });
  }, [email, hasTarget, username, unsubscribeMutation]);

  const renderContent = () => {
    if (!hasTarget) {
      return (
        <>
          <h1 className="text-2xl sm:text-3xl font-semibold text-content dark:text-content-dark">
            {t('Invalid unsubscribe link')}
          </h1>
          <p className="mt-3 text-sm sm:text-base text-content-secondary dark:text-content-secondary-dark">
            {t('The link is missing the required unsubscribe parameters.')}
          </p>
        </>
      );
    }

    if (unsubscribeMutation.isPending) {
      return (
        <>
          <h1 className="text-2xl sm:text-3xl font-semibold text-content dark:text-content-dark">
            {t('Processing your request')}
          </h1>
          <p className="mt-3 text-sm sm:text-base text-content-secondary dark:text-content-secondary-dark">
            {t('We are unsubscribing you from marketing emails.')}
          </p>
        </>
      );
    }

    if (unsubscribeMutation.isError) {
      return (
        <>
          <h1 className="text-2xl sm:text-3xl font-semibold text-content dark:text-content-dark">
            {t('Unable to unsubscribe')}
          </h1>
          <p className="mt-3 text-sm sm:text-base text-content-secondary dark:text-content-secondary-dark">
            {t('Please try again in a moment.')}
          </p>
          <button
            type="button"
            onClick={() => unsubscribeMutation.mutate({ username: username || undefined, email: email || undefined })}
            className="mt-6 inline-flex items-center justify-center rounded-xl bg-button dark:bg-button-dark px-5 py-3 text-sm font-medium text-button-text dark:text-button-text-dark hover:bg-button-hover dark:hover:bg-button-hover-dark"
          >
            {t('Try again')}
          </button>
        </>
      );
    }

    return (
      <>
        <h1 className="text-2xl sm:text-3xl font-semibold text-content dark:text-content-dark">
          {t('You are unsubscribed')}
        </h1>
        <p className="mt-3 text-sm sm:text-base text-content-secondary dark:text-content-secondary-dark">
          {t('Marketing emails are now disabled for your account.')}
        </p>
      </>
    );
  };

  return (
    <div className="min-h-[calc(100vh-220px)] bg-surface dark:bg-surface-dark px-4 py-12 sm:py-20">
      <div className="mx-auto max-w-2xl rounded-lg border border-outline dark:border-outline-dark bg-surface dark:bg-surface-dark p-6 sm:p-10">
        {renderContent()}
        <div className="mt-8">
          <Link
            to={ROUTES.HOME}
            className="inline-flex items-center justify-center rounded-xl border border-outline dark:border-outline-dark px-5 py-3 text-sm font-medium text-content dark:text-content-dark hover:bg-surface dark:hover:bg-surface-dark"
          >
            {t('Back to home')}
          </Link>
        </div>
      </div>
    </div>
  );
};
