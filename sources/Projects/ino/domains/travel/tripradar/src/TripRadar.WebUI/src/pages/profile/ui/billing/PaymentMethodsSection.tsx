import { useState } from 'react';
import { CreditCard, Plus } from 'lucide-react';
import { useFrontendLanguage } from 'app/providers';
import { useToast } from 'app/providers/ToastProvider';
import {
  useDeletePaymentMethodMutation,
  usePaymentMethodsQuery,
  useSetDefaultPaymentMethodMutation,
} from 'entities/payment/api';
import { AddCardForm } from 'features/payment';
import { Button, SectionEmpty, SectionError } from 'shared/ui';
import { PaymentMethodCard } from './PaymentMethodCard';
import { PaymentMethodsSkeleton } from './PaymentMethodsSkeleton';

export type PaymentMethod = NonNullable<
  NonNullable<ReturnType<typeof usePaymentMethodsQuery>['data']>['paymentMethods']
>[number];

export const PaymentMethodsSection = () => {
  const { t } = useFrontendLanguage();
  const { showSuccess, showError } = useToast();
  const [isAddingCard, setIsAddingCard] = useState(false);
  const [deletingMethodId, setDeletingMethodId] = useState<string | null>(null);

  const {
    data: paymentMethodsData,
    isLoading: isLoadingMethods,
    error: methodsError,
    refetch: refetchMethods,
  } = usePaymentMethodsQuery();
  const setDefaultMethod = useSetDefaultPaymentMethodMutation();
  const deleteMethod = useDeletePaymentMethodMutation();

  const handleSetDefault = (method: PaymentMethod) => {
    setDefaultMethod.mutate(
      {
        brand: method.card?.brand,
        last4: method.card?.last4,
        expMonth: method.card?.expMonth,
        expYear: method.card?.expYear,
        setAsDefault: true,
      },
      {
        onSuccess: () => showSuccess(t('Payment method updated'), t('Default payment method changed')),
        onError: err => {
          const msg = err instanceof Error ? err.message : t('Failed to set default payment method');
          showError(t('Update failed'), msg);
        },
      }
    );
  };

  const handleDeleteMethod = (method: PaymentMethod) => {
    deleteMethod.mutate(
      {
        brand: method.card?.brand,
        last4: method.card?.last4 || '',
        expMonth: method.card?.expMonth,
        expYear: method.card?.expYear,
      },
      {
        onSuccess: () => {
          setDeletingMethodId(null);
          showSuccess(t('Payment method deleted'), t('The payment method has been removed'));
        },
        onError: err => {
          const msg = err instanceof Error ? err.message : t('Failed to delete payment method');
          showError(t('Delete failed'), msg);
        },
      }
    );
  };

  const methods = paymentMethodsData?.paymentMethods;
  const hasMethods = methods && methods.length > 0;

  if (isLoadingMethods) {
    return <PaymentMethodsSkeleton />;
  }

  if (methodsError) {
    return <SectionError message={t('Failed to load payment methods')} onRetry={() => refetchMethods()} />;
  }

  return (
    <div>
      <h3 className="text-sm font-medium text-content-secondary dark:text-content-secondary-dark mb-1">
        {t('Payment Methods')}
      </h3>

      {hasMethods ? (
        <div>
          {methods.map(method => (
            <PaymentMethodCard
              key={method.id}
              method={method}
              isDeleting={deletingMethodId === method.id}
              isDeleteBlocked={deleteMethod.isPending && deletingMethodId !== method.id}
              isDeletePending={deleteMethod.isPending && deletingMethodId === method.id}
              isSetDefaultPending={setDefaultMethod.isPending}
              onSetDefault={() => handleSetDefault(method)}
              onDeleteRequest={() => setDeletingMethodId(method.id || null)}
              onDeleteConfirm={() => handleDeleteMethod(method)}
              onDeleteCancel={() => setDeletingMethodId(null)}
            />
          ))}
        </div>
      ) : !isAddingCard ? (
        <SectionEmpty
          message={t('No payment methods added yet')}
          icon={<CreditCard className="h-6 w-6 text-content-muted dark:text-content-muted-dark" />}
          action={
            <Button variant="primary" size="sm" onClick={() => setIsAddingCard(true)}>
              {t('Add Card')}
            </Button>
          }
        />
      ) : null}

      {isAddingCard ? (
        <div className="mt-3 pt-3 border-t border-outline dark:border-outline-dark">
          <AddCardForm onSuccess={() => setIsAddingCard(false)} onCancel={() => setIsAddingCard(false)} />
        </div>
      ) : (
        <button
          type="button"
          onClick={() => setIsAddingCard(true)}
          className="inline-flex items-center gap-1.5 mt-3 text-sm text-content-muted dark:text-content-muted-dark hover:text-content dark:hover:text-content-dark transition-colors"
        >
          <Plus className="h-3.5 w-3.5" />
          {t('Add Card')}
        </button>
      )}
    </div>
  );
};
