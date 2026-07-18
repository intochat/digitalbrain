import { useFrontendLanguage } from 'app/providers';
import { RefundForm } from 'features/payment';

export const RefundSection = () => {
  const { t } = useFrontendLanguage();

  return (
    <div className="border-b border-outline dark:border-outline-dark pb-6 sm:pb-8">
      <h3 className="text-base sm:text-lg font-medium text-content dark:text-content-dark mb-3 sm:mb-4">
        {t('Request a Refund')}
      </h3>
      <div className="p-4 sm:p-6 bg-surface-accent dark:bg-surface-accent-dark border border-outline dark:border-outline-dark rounded-xl">
        <RefundForm />
      </div>
    </div>
  );
};
