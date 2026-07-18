import { useFrontendLanguage } from 'app/providers';

export const LoginSuccessState = () => {
  const { t } = useFrontendLanguage();

  return (
    <div
      className="mb-5 rounded-lg border border-outline dark:border-outline-dark bg-surface-accent dark:bg-surface-accent-dark p-3"
      role="status"
      aria-live="polite"
      aria-atomic="true"
    >
      <p className="text-sm font-medium text-content dark:text-content-dark">{t('Login successful!')}</p>
      <p className="text-xs text-content-secondary dark:text-content-secondary-dark mt-0.5">
        {t('Redirecting you now...')}
      </p>
    </div>
  );
};
