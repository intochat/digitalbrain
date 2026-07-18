import { ArrowRightLeft } from 'lucide-react';
import { useFrontendLanguage } from 'app/providers';

interface RetentionStepProps {
  currentTierType: string;
  onSwitchToChangePlan: () => void;
  onContinueCancel: () => void;
}

export const RetentionStep = ({ currentTierType, onSwitchToChangePlan, onContinueCancel }: RetentionStepProps) => {
  const { t } = useFrontendLanguage();

  return (
    <div className="space-y-5">
      <div className="space-y-2">
        <h3 className="text-sm font-medium text-content dark:text-content-dark">{t('Before you go...')}</h3>
        <p className="text-sm text-content-secondary dark:text-content-secondary-dark leading-relaxed">
          {t(
            "You're currently on the {plan} plan. Instead of cancelling, consider switching to a more affordable plan to keep access to premium features.",
            { plan: currentTierType }
          )}
        </p>
      </div>

      <div className="flex flex-col gap-2">
        <button
          type="button"
          onClick={onSwitchToChangePlan}
          className="w-full px-4 py-2 text-sm font-medium rounded-lg bg-button dark:bg-button-dark text-button-text dark:text-button-text-dark hover:bg-button-hover dark:hover:bg-button-hover-dark transition-colors flex items-center justify-center gap-2"
        >
          <ArrowRightLeft className="h-3.5 w-3.5" />
          {t('Consider another plan')}
        </button>
        <button
          type="button"
          onClick={onContinueCancel}
          className="w-full px-4 py-2 text-sm font-medium rounded-lg text-content-muted dark:text-content-muted-dark hover:text-content dark:hover:text-content-dark hover:bg-surface-accent dark:hover:bg-surface-accent-dark transition-colors"
        >
          {t('Continue cancellation')}
        </button>
      </div>
    </div>
  );
};
