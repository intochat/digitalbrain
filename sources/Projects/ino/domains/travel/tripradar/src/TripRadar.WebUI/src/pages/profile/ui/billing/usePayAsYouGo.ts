import { useQueryClient } from '@tanstack/react-query';
import { useFrontendLanguage } from 'app/providers';
import { useToast } from 'app/providers/ToastProvider';
import { paymentKeys, useOverageUsageQuery, useTogglePayAsYouGoMutation } from 'entities/payment/api';
import type { OverageUsageResponse } from 'shared/api';

interface UsePayAsYouGoResult {
  isPayAsYouGoOn: boolean;
  handleToggle: () => void;
  isPending: boolean;
  isLoading: boolean;
}

export const usePayAsYouGo = (): UsePayAsYouGoResult => {
  const { t } = useFrontendLanguage();
  const { showSuccess, showError } = useToast();
  const queryClient = useQueryClient();
  const { data: overageUsage, isLoading } = useOverageUsageQuery();
  const togglePayAsYouGo = useTogglePayAsYouGoMutation();

  const isPayAsYouGoOn = overageUsage?.payAsYouGoEnabled ?? false;

  const handleToggle = () => {
    if (togglePayAsYouGo.isPending) return;
    const newEnabled = !isPayAsYouGoOn;
    const previousOverage = queryClient.getQueryData<OverageUsageResponse>(paymentKeys.overageUsage());

    queryClient.setQueryData<OverageUsageResponse>(paymentKeys.overageUsage(), old =>
      old ? { ...old, payAsYouGoEnabled: newEnabled } : old
    );

    togglePayAsYouGo.mutate(
      { enabled: newEnabled },
      {
        onSuccess: res => {
          const msg = res?.enabled ? t('Pay as you go is enabled.') : t('Pay as you go is disabled.');
          showSuccess(t('Settings updated'), msg);
        },
        onError: err => {
          queryClient.setQueryData(paymentKeys.overageUsage(), previousOverage);
          const message =
            err instanceof Error ? err.message : t('Failed to update pay as you go settings. Please try again.');
          showError(t('Update failed'), message);
        },
      }
    );
  };

  return { isPayAsYouGoOn, handleToggle, isPending: togglePayAsYouGo.isPending, isLoading };
};
