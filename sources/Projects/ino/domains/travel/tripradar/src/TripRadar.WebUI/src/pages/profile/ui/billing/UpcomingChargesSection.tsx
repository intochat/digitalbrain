import { useFrontendLanguage } from 'app/providers';

export const UpcomingChargesSection = () => {
  const { t } = useFrontendLanguage();

  return (
    <div>
      <h3 className="text-lg font-medium text-content dark:text-content-dark mb-4">{t('Upcoming Charges')}</h3>
      <div className="p-6 bg-surface-accent dark:bg-surface-accent-dark border border-outline dark:border-outline-dark rounded-xl">
        <div className="flex items-center justify-center py-8">
          <div className="text-center">
            <div className="w-12 h-12 bg-primary-100 dark:bg-primary-500/20 rounded-xl flex items-center justify-center mx-auto mb-4">
              <svg
                className="w-6 h-6 text-primary-600 dark:text-primary-400"
                fill="none"
                stroke="currentColor"
                viewBox="0 0 24 24"
              >
                <path
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  strokeWidth={2}
                  d="M8 7V3m8 4V3m-9 8h10M5 21h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v12a2 2 0 002 2z"
                />
              </svg>
            </div>
            <h4 className="font-medium text-content dark:text-content-dark mb-2">
              {t('Upcoming Charges Coming Soon')}
            </h4>
            <p className="text-sm text-content-secondary dark:text-content-secondary-dark mb-1">
              {t('Information about upcoming billing cycles will be shown here')}
            </p>
            <p className="text-xs text-content-muted">
              {t('View next billing date, amount, and manage payment methods')}
            </p>
          </div>
        </div>
      </div>
    </div>
  );
};
