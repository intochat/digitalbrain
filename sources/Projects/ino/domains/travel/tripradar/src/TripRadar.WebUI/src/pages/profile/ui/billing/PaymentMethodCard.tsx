import { CreditCard, Loader2, Trash2 } from 'lucide-react';
import { useFrontendLanguage } from 'app/providers';
import { Button } from 'shared/ui';
import { capitalize } from './billingUtils';
import type { PaymentMethod } from './PaymentMethodsSection';

interface PaymentMethodCardProps {
  method: PaymentMethod;
  isDeleting: boolean;
  isDeleteBlocked: boolean;
  isDeletePending: boolean;
  isSetDefaultPending: boolean;
  onSetDefault: () => void;
  onDeleteRequest: () => void;
  onDeleteConfirm: () => void;
  onDeleteCancel: () => void;
}

export const PaymentMethodCard = ({
  method,
  isDeleting,
  isDeleteBlocked,
  isDeletePending,
  isSetDefaultPending,
  onSetDefault,
  onDeleteRequest,
  onDeleteConfirm,
  onDeleteCancel,
}: PaymentMethodCardProps) => {
  const { t } = useFrontendLanguage();
  const brand = capitalize(method.card?.brand || 'Card');
  const last4 = method.card?.last4 || '';
  const expiry = `${String(method.card?.expMonth).padStart(2, '0')}/${String(method.card?.expYear).slice(-2)}`;

  if (isDeleting) {
    return (
      <div className="flex items-center justify-between py-2 px-3 rounded-lg bg-surface-accent dark:bg-surface-accent-dark">
        <p className="text-sm text-content dark:text-content-dark">{t('Delete this card?')}</p>
        <div className="flex items-center gap-2">
          <Button variant="destructive" size="sm" onClick={onDeleteConfirm} disabled={isDeletePending}>
            {isDeletePending && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
            {t('Confirm')}
          </Button>
          <Button variant="ghost" size="sm" onClick={onDeleteCancel}>
            {t('Cancel')}
          </Button>
        </div>
      </div>
    );
  }

  return (
    <div className="flex items-center justify-between py-2 group">
      <div className="flex items-center gap-3">
        <CreditCard className="h-4 w-4 text-content-muted dark:text-content-muted-dark flex-shrink-0" />
        <div className="flex items-center gap-2">
          <span className="text-sm text-content dark:text-content-dark">
            {brand} •••• {last4}
          </span>
          <span className="text-xs text-content-muted dark:text-content-muted-dark">{expiry}</span>
          {method.isDefault && (
            <span className="text-xs text-content-muted dark:text-content-muted-dark">· {t('Default')}</span>
          )}
        </div>
      </div>
      <div className="flex items-center gap-1 opacity-0 group-hover:opacity-100 transition-opacity">
        {!method.isDefault && (
          <button
            type="button"
            onClick={onSetDefault}
            disabled={isSetDefaultPending}
            className="inline-flex items-center gap-1.5 px-2.5 py-1 text-xs text-content-secondary dark:text-content-secondary-dark hover:text-content dark:hover:text-content-dark disabled:opacity-50 transition-colors touch-manipulation"
          >
            {isSetDefaultPending && <Loader2 className="h-3 w-3 animate-spin" />}
            {t('Set as default')}
          </button>
        )}
        <button
          type="button"
          onClick={onDeleteRequest}
          disabled={isDeleteBlocked || isDeletePending}
          aria-label={`Delete ${brand} •••• ${last4}`}
          className="inline-flex items-center justify-center h-7 w-7 text-content-muted dark:text-content-muted-dark hover:text-red-500 dark:hover:text-red-400 disabled:opacity-50 rounded-md transition-colors touch-manipulation"
        >
          <Trash2 className="h-3.5 w-3.5" />
        </button>
      </div>
    </div>
  );
};
