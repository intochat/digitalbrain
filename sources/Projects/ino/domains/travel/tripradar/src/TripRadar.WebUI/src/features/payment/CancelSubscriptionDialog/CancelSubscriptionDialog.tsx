import { useState } from 'react';
import { Loader2 } from 'lucide-react';
import { useFrontendLanguage } from 'app/providers';
import { useToast } from 'app/providers/ToastProvider';
import { useCancelSubscriptionMutation } from 'entities/payment/api';
import { getFeatureDiff } from 'shared/config/pricing/billingUtils';
import { Textarea } from 'shared/ui';
import { Modal } from 'shared/ui';
import { RetentionStep } from './RetentionStep';

type CancelStep = 'retention' | 'confirmation';

interface CancelSubscriptionDialogProps {
  isOpen: boolean;
  onClose: () => void;
  currentPeriodEnd?: string | null;
  currentTierType: string;
  onSwitchToChangePlan: () => void;
}

export const CancelSubscriptionDialog = ({
  isOpen,
  onClose,
  currentPeriodEnd,
  currentTierType,
  onSwitchToChangePlan,
}: CancelSubscriptionDialogProps) => {
  const { t, language } = useFrontendLanguage();
  const { showSuccess, showError } = useToast();
  const [reason, setReason] = useState('');
  const [step, setStep] = useState<CancelStep>('retention');
  const cancelSubscription = useCancelSubscriptionMutation();

  const isPending = cancelSubscription.isPending;
  const lostFeatures = getFeatureDiff(currentTierType, 'basic');

  const formattedEndDate = currentPeriodEnd
    ? new Date(currentPeriodEnd).toLocaleDateString(language === 'ru' ? 'ru-RU' : 'en-US', {
        year: 'numeric',
        month: 'long',
        day: 'numeric',
      })
    : null;

  const handleConfirm = () => {
    if (isPending) return;

    cancelSubscription.mutate(
      { cancellationReason: reason.trim() || undefined },
      {
        onSuccess: () => {
          showSuccess(t('Subscription cancelled'), t('Your subscription has been cancelled successfully'));
          setReason('');
          setStep('retention');
          onClose();
        },
        onError: err => {
          const message = err instanceof Error ? err.message : t('Failed to cancel subscription');
          showError(t('Cancellation failed'), message);
        },
      }
    );
  };

  const handleClose = () => {
    if (isPending) return;
    setReason('');
    setStep('retention');
    onClose();
  };

  return (
    <Modal isOpen={isOpen} onClose={handleClose} title={t('Cancel Subscription')} size="md">
      {step === 'retention' ? (
        <RetentionStep
          currentTierType={currentTierType}
          onSwitchToChangePlan={onSwitchToChangePlan}
          onContinueCancel={() => setStep('confirmation')}
        />
      ) : (
        <div className="space-y-4">
          <div className="rounded-lg border border-outline/50 dark:border-outline-dark/50 bg-surface-accent dark:bg-surface-accent-dark p-3">
            <div className="text-sm text-content-secondary dark:text-content-secondary-dark space-y-1">
              <p>
                {t(
                  'Cancelling your subscription will revoke access to premium features at the end of your current billing period.'
                )}
              </p>
              {formattedEndDate && (
                <p className="font-medium text-content dark:text-content-dark">
                  {t('Your access to premium features will end on {date}.', { date: formattedEndDate })}
                </p>
              )}
            </div>
          </div>

          {lostFeatures.length > 0 && (
            <div>
              <p className="text-xs font-medium text-content-muted dark:text-content-muted-dark mb-2">
                {t('Features you will lose after cancellation')}
              </p>
              <ul className="space-y-1" role="list" aria-label={t('Features you will lose')}>
                {lostFeatures.map(feature => (
                  <li
                    key={feature}
                    className="flex items-start gap-2 text-sm text-content-secondary dark:text-content-secondary-dark"
                  >
                    <span className="text-content-muted dark:text-content-muted-dark mt-0.5">·</span>
                    {t(feature)}
                  </li>
                ))}
              </ul>
            </div>
          )}

          <div>
            <label
              htmlFor="cancel-reason"
              className="block text-xs font-medium text-content-secondary dark:text-content-secondary-dark mb-1.5"
            >
              {t('Reason for cancellation')}
              <span className="text-content-muted dark:text-content-muted-dark font-normal ml-1">
                ({t('optional')})
              </span>
            </label>
            <Textarea
              id="cancel-reason"
              value={reason}
              onChange={e => setReason(e.target.value)}
              placeholder={t('Tell us why you are cancelling...')}
              disabled={isPending}
              rows={3}
              className="resize-none"
            />
          </div>

          <div className="flex justify-end gap-2 pt-1">
            <button
              type="button"
              onClick={handleClose}
              disabled={isPending}
              className="px-4 py-2 text-sm font-medium rounded-lg text-content-secondary dark:text-content-secondary-dark hover:text-content dark:hover:text-content-dark hover:bg-surface-accent dark:hover:bg-surface-accent-dark transition-colors disabled:opacity-50"
            >
              {t('Keep Subscription')}
            </button>
            <button
              type="button"
              onClick={handleConfirm}
              disabled={isPending}
              className="px-4 py-2 text-sm font-medium rounded-lg bg-red-600 dark:bg-red-500 text-white hover:bg-red-700 dark:hover:bg-red-600 transition-colors disabled:opacity-50 flex items-center gap-2"
            >
              {isPending && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
              {t('Cancel Subscription')}
            </button>
          </div>
        </div>
      )}
    </Modal>
  );
};
