import { RefreshCw } from 'lucide-react';
import { useFrontendLanguage } from 'app/providers';
import { useToast } from 'app/providers/ToastProvider';
import { useOverageUsageQuery, useTogglePayAsYouGoMutation, useUsageSummaryQuery } from 'entities/payment/api';
import { Switch } from 'shared/ui';
import { capitalize, formatPrice } from './billingUtils';
import { ProgressBar } from './ProgressBar';
import { useTierInfo } from './useTierInfo';

export const UsageSection = () => {
  const { t } = useFrontendLanguage();
  const { showSuccess, showError } = useToast();
  const { isBasicTier } = useTierInfo();
  const {
    data: usageSummary,
    isLoading: isLoadingUsage,
    error: usageError,
    refetch: refetchUsage,
  } = useUsageSummaryQuery();
  const {
    data: overageData,
    isLoading: isLoadingOverage,
    error: overageError,
    refetch: refetchOverage,
  } = useOverageUsageQuery();
  const togglePayAsYouGo = useTogglePayAsYouGoMutation();

  const handleTogglePayAsYouGo = () => {
    if (togglePayAsYouGo.isPending) return;
    const newEnabled = !usageSummary?.hasMeteredBilling;
    togglePayAsYouGo.mutate(
      { enabled: newEnabled },
      {
        onSuccess: () => {
          const msg = newEnabled ? t('Pay-As-You-Go enabled') : t('Pay-As-You-Go disabled');
          showSuccess(t('Usage settings updated'), msg);
        },
        onError: err => {
          const message = err instanceof Error ? err.message : t('Failed to update Pay-As-You-Go');
          showError(t('Update failed'), message);
        },
      }
    );
  };

  return (
    <div className="border-b border-outline dark:border-outline-dark pb-6 sm:pb-8">
      <h3 className="text-base sm:text-lg font-medium text-content dark:text-content-dark mb-3 sm:mb-4">
        {t('Usage')}
      </h3>

      {isLoadingUsage ? (
        <div className="p-6 bg-surface-accent dark:bg-surface-accent-dark border border-outline dark:border-outline-dark rounded-xl">
          <div className="animate-pulse space-y-4">
            <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
              <div className="h-16 bg-outline dark:bg-outline-dark rounded" />
              <div className="h-16 bg-outline dark:bg-outline-dark rounded" />
              <div className="h-16 bg-outline dark:bg-outline-dark rounded" />
            </div>
            <div className="h-2 bg-outline dark:bg-outline-dark rounded w-full" />
            <div className="h-4 bg-outline dark:bg-outline-dark rounded w-1/3" />
          </div>
        </div>
      ) : usageError ? (
        <div className="p-6 bg-surface-accent dark:bg-surface-accent-dark border border-outline dark:border-outline-dark rounded-xl">
          <div className="flex flex-col items-center justify-center py-4 text-center">
            <p className="text-sm text-red-600 dark:text-red-400 mb-3">{t('Failed to load usage data')}</p>
            <button
              type="button"
              onClick={() => refetchUsage()}
              className="inline-flex items-center gap-2 px-4 py-2 text-sm font-medium rounded-lg border border-outline dark:border-outline-dark text-content dark:text-content-dark hover:bg-surface-accent dark:hover:bg-surface-accent-dark transition-colors"
            >
              <RefreshCw className="h-4 w-4" />
              {t('Retry')}
            </button>
          </div>
        </div>
      ) : (
        <div className="p-4 sm:p-6 bg-surface-accent dark:bg-surface-accent-dark border border-outline dark:border-outline-dark rounded-xl">
          {usageSummary?.currentPeriod && (
            <div className="mb-4 text-sm text-content-secondary dark:text-content-secondary-dark">
              {t('Billing period: {start} — {end} ({days} days remaining)', {
                start: new Date(usageSummary.currentPeriod.start!).toLocaleDateString(),
                end: new Date(usageSummary.currentPeriod.end!).toLocaleDateString(),
                days: usageSummary.currentPeriod.daysRemaining ?? 0,
              })}
            </div>
          )}

          {usageSummary?.usage && Object.keys(usageSummary.usage).length > 0 && (
            <div className="space-y-4 mb-4">
              {Object.entries(usageSummary.usage).map(([metricName, metric]) => {
                const used = metric.used ?? 0;
                const limit = metric.limit ?? 0;
                const percentage = limit > 0 ? (used / limit) * 100 : 0;
                return (
                  <div key={metricName}>
                    <div className="flex justify-between items-center mb-1">
                      <span className="text-sm font-medium text-content dark:text-content-dark">
                        {t(capitalize(metricName))}
                      </span>
                      <span className="text-sm text-content-secondary dark:text-content-secondary-dark">
                        {used.toLocaleString()} / {limit.toLocaleString()} {metric.unit || ''}
                      </span>
                    </div>
                    <ProgressBar percentage={percentage} />
                  </div>
                );
              })}
            </div>
          )}

          {/* Pay-As-You-Go toggle */}
          <div className="flex items-center justify-between pt-4 border-t border-outline dark:border-outline-dark">
            <div>
              <p className="text-sm font-medium text-content dark:text-content-dark">{t('Pay-As-You-Go')}</p>
              <p className="text-xs text-content-secondary dark:text-content-secondary-dark">
                {usageSummary?.hasMeteredBilling
                  ? t('You will be charged for usage beyond your plan limits')
                  : t('Enable to continue using services after reaching your plan limits')}
              </p>
            </div>
            <Switch
              checked={!!usageSummary?.hasMeteredBilling}
              onChange={handleTogglePayAsYouGo}
              loading={togglePayAsYouGo.isPending}
              aria-label={t('Pay-As-You-Go')}
            />
          </div>

          {/* Overage usage — only for paid subscriptions */}
          {!isBasicTier && (
            <div className="mt-4 pt-4 border-t border-outline dark:border-outline-dark">
              {isLoadingOverage ? (
                <div className="animate-pulse space-y-2">
                  <div className="h-4 bg-outline dark:bg-outline-dark rounded w-1/3" />
                  <div className="h-4 bg-outline dark:bg-outline-dark rounded w-1/2" />
                </div>
              ) : overageError ? (
                <div className="flex flex-col items-center justify-center py-2 text-center">
                  <p className="text-sm text-red-600 dark:text-red-400 mb-2">{t('Failed to load overage data')}</p>
                  <button
                    type="button"
                    onClick={() => refetchOverage()}
                    className="inline-flex items-center gap-2 px-3 py-1.5 text-sm font-medium rounded-lg border border-outline dark:border-outline-dark text-content dark:text-content-dark hover:bg-surface-accent dark:hover:bg-surface-accent-dark transition-colors"
                  >
                    <RefreshCw className="h-3.5 w-3.5" />
                    {t('Retry')}
                  </button>
                </div>
              ) : overageData ? (
                <div>
                  <p className="text-sm font-medium text-content dark:text-content-dark mb-2">{t('Overage Usage')}</p>
                  <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
                    <div className="text-center p-3 border border-outline dark:border-outline-dark rounded-lg">
                      <p className="text-lg font-semibold text-content dark:text-content-dark">
                        {(overageData.regularTokensUsed ?? 0).toLocaleString()}
                      </p>
                      <p className="text-xs text-content-secondary dark:text-content-secondary-dark">
                        {t('Regular tokens used')}
                      </p>
                    </div>
                    <div className="text-center p-3 border border-outline dark:border-outline-dark rounded-lg">
                      <p className="text-lg font-semibold text-content dark:text-content-dark">
                        {(overageData.overageTokensUsed ?? 0).toLocaleString()}
                      </p>
                      <p className="text-xs text-content-secondary dark:text-content-secondary-dark">
                        {t('Overage tokens used')}
                      </p>
                    </div>
                    <div className="text-center p-3 border border-outline dark:border-outline-dark rounded-lg">
                      <p className="text-lg font-semibold text-content dark:text-content-dark">
                        {formatPrice(overageData.totalOverageCharges, overageData.currency)}
                      </p>
                      <p className="text-xs text-content-secondary dark:text-content-secondary-dark">
                        {t('Total overage charges')}
                      </p>
                    </div>
                  </div>
                </div>
              ) : null}
            </div>
          )}
        </div>
      )}
    </div>
  );
};
