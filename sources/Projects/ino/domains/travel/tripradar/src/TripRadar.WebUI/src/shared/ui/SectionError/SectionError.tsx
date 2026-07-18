import { RefreshCw } from 'lucide-react';
import { useFrontendLanguage } from 'app/providers/FrontendLanguageContext';

interface SectionErrorProps {
  message: string;
  onRetry: () => void;
}

export const SectionError = ({ message, onRetry }: SectionErrorProps) => {
  const { t } = useFrontendLanguage();

  return (
    <div role="alert" className="border border-outline dark:border-outline-dark rounded-xl p-6">
      <div className="flex flex-col items-center justify-center py-4 text-center">
        <p className="text-sm text-content-secondary dark:text-content-secondary-dark mb-3">{message}</p>
        <button
          type="button"
          onClick={onRetry}
          className="touch-manipulation inline-flex items-center gap-2 px-4 py-2 text-sm font-medium rounded-lg border border-outline dark:border-outline-dark text-content dark:text-content-dark hover:bg-surface-accent dark:hover:bg-surface-accent-dark transition-colors"
        >
          <RefreshCw className="h-4 w-4" />
          {t('Retry')}
        </button>
      </div>
    </div>
  );
};
