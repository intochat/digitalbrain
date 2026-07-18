import { type FormEvent, useMemo, useState } from 'react';
import { useFrontendLanguage } from 'app/providers';
import { useToast } from 'app/providers/ToastProvider';
import { useCreateRefundMutation } from 'entities/payment/api';
import { Button, Dropdown, Textarea } from 'shared/ui';
import type { DropdownOption } from 'shared/ui';

const REFUND_REASONS = ['duplicate', 'fraudulent', 'subscriptionCanceled', 'serviceNotDelivered'] as const;

type RefundReason = (typeof REFUND_REASONS)[number];

const REASON_LABELS: Record<RefundReason, string> = {
  duplicate: 'Duplicate charge',
  fraudulent: 'Fraudulent charge',
  subscriptionCanceled: 'Subscription cancelled',
  serviceNotDelivered: 'Service not delivered',
};

export const RefundForm = () => {
  const { t } = useFrontendLanguage();
  const { showSuccess, showError } = useToast();
  const [reason, setReason] = useState<RefundReason | ''>('');
  const [comment, setComment] = useState('');
  const createRefund = useCreateRefundMutation();

  const isPending = createRefund.isPending;
  const error = createRefund.error;

  const reasonOptions: DropdownOption[] = useMemo(
    () => REFUND_REASONS.map(r => ({ value: r, label: t(REASON_LABELS[r]) })),
    [t]
  );

  const handleSubmit = (e: FormEvent) => {
    e.preventDefault();
    if (!reason || isPending) return;

    createRefund.mutate(
      {
        reason,
        metadata: comment.trim() ? { comment: comment.trim() } : undefined,
      },
      {
        onSuccess: () => {
          showSuccess(t('Refund requested'), t('Your refund request has been submitted successfully'));
          setReason('');
          setComment('');
        },
        onError: err => {
          const message = err instanceof Error ? err.message : t('Failed to submit refund request');
          showError(t('Refund failed'), message);
        },
      }
    );
  };

  return (
    <form onSubmit={handleSubmit} className="space-y-4">
      <div className="flex flex-col gap-1.5">
        <span className="text-sm font-medium text-content dark:text-content-dark">{t('Reason for refund')}</span>
        <Dropdown
          value={reason}
          options={reasonOptions}
          onChange={v => setReason(v as RefundReason)}
          disabled={isPending}
          placeholder={t('Select a reason')}
          aria-label={t('Reason for refund')}
        />
      </div>

      <div className="flex flex-col gap-1.5">
        <span className="text-sm font-medium text-content dark:text-content-dark">
          {t('Additional comments')}{' '}
          <span className="text-content-secondary dark:text-content-secondary-dark font-normal">({t('optional')})</span>
        </span>
        <Textarea
          value={comment}
          onChange={e => setComment(e.target.value)}
          placeholder={t('Provide any additional details...')}
          disabled={isPending}
          rows={3}
        />
      </div>

      {error && (
        <p className="text-sm text-red-600 dark:text-red-400">
          {error instanceof Error ? error.message : t('An error occurred')}
        </p>
      )}

      <div className="flex justify-end">
        <Button type="submit" disabled={!reason || isPending} isLoading={isPending}>
          {t('Submit Refund Request')}
        </Button>
      </div>
    </form>
  );
};
