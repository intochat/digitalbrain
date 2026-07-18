import { useQueryClient } from '@tanstack/react-query';
import { useFrontendLanguage } from 'app/providers';
import { useToast } from 'app/providers/ToastProvider';
import { paymentKeys, useToggleSubscriptionMutation } from 'entities/payment/api';
import type { GetUserSubscriptionResponse } from 'shared/api';

interface UseAutoRenewalParams {
  subscription: GetUserSubscriptionResponse | undefined;
}

interface UseAutoRenewalResult {
  isAutoRenewalOn: boolean;
  handleToggle: () => void;
  isPending: boolean;
}

export const useAutoRenewal = ({ subscription }: UseAutoRenewalParams): UseAutoRenewalResult => {
  const { t } = useFrontendLanguage();
  const { showSuccess, showError } = useToast();
  const queryClient = useQueryClient();
  const toggleSubscription = useToggleSubscriptionMutation();

  const isAutoRenewalOn = subscription ? !subscription.cancelAtPeriodEnd : false;

  const handleToggle = () => {
    if (!subscription || toggleSubscription.isPending) return;
    const newCancelAtPeriodEnd = isAutoRenewalOn;
    const previousSubscription = queryClient.getQueryData<GetUserSubscriptionResponse>(paymentKeys.subscription());

    queryClient.setQueryData<GetUserSubscriptionResponse>(paymentKeys.subscription(), old =>
      old ? { ...old, cancelAtPeriodEnd: newCancelAtPeriodEnd } : old
    );

    toggleSubscription.mutate(
      { activate: !isAutoRenewalOn },
      {
        onSuccess: res => {
          const msg = res?.message ? t(res.message) : t('Auto-renewal updated');
          showSuccess(t('Subscription updated'), msg);
        },
        onError: err => {
          queryClient.setQueryData(paymentKeys.subscription(), previousSubscription);
          const message = err instanceof Error ? err.message : t('Failed to update auto-renewal');
          showError(t('Update failed'), message);
        },
      }
    );
  };

  return { isAutoRenewalOn, handleToggle, isPending: toggleSubscription.isPending };
};
