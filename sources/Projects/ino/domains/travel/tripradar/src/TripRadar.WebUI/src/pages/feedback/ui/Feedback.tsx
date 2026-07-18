import { useFrontendLanguage } from 'app/providers';
import { FeedbackSection } from 'features/feedback';

export const Feedback = () => {
  const { t } = useFrontendLanguage();

  return (
    <div className="flex-1 bg-surface dark:bg-surface-dark transition-colors duration-150 pt-24">
      <div className="max-w-3xl mx-auto px-4 sm:px-6 lg:px-8 py-12 space-y-4">
        <header>
          <h1 className="text-lg font-semibold text-content dark:text-content-dark">{t('Feedback')}</h1>
          <p className="text-sm text-content-secondary dark:text-content-secondary-dark mt-1">
            {t('Share product feedback, report issues, or suggest improvements.')}
          </p>
        </header>

        <FeedbackSection />
      </div>
    </div>
  );
};
