import { AlertCircle } from 'lucide-react';
import { useFrontendLanguage } from 'app/providers';

export interface NoTraceBannerProps {
  visible: boolean;
}

export const NoTraceBanner = ({ visible }: NoTraceBannerProps) => {
  const { t } = useFrontendLanguage();

  if (!visible) return null;

  return (
    <div className="rounded-lg border border-amber-200/70 bg-amber-50/70 p-4 sm:p-5 dark:border-amber-500/30 dark:bg-amber-500/10">
      <p className="inline-flex items-start gap-2 text-sm sm:text-base font-semibold text-amber-900 dark:text-amber-200">
        <AlertCircle className="h-4 w-4 mt-0.5 shrink-0" />
        <span>{t('No-trace mode is enabled: private requests are excluded from this page by design.')}</span>
      </p>
    </div>
  );
};
