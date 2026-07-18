import { TrendingDown, TrendingUp } from 'lucide-react';
import { useFrontendLanguage } from 'app/providers';
import type { GetUserSubscriptionResponse } from 'shared/api';
import { clampPercent, formatDateTime } from './usageUtils';
import type { PeriodComparison } from './useUsageData';

interface UsageBalanceCardProps {
  title: string;
  headline: string;
  progressPercent: number | null;
  resetLabel: string;
}

const UsageBalanceCard = ({ title, headline, progressPercent, resetLabel }: UsageBalanceCardProps) => {
  const safePercent = progressPercent == null ? 0 : clampPercent(progressPercent);

  return (
    <div className="space-y-3">
      <p className="text-xs font-medium text-content-muted dark:text-content-muted-dark">{title}</p>
      <p className="text-xl font-semibold text-content dark:text-content-dark">{headline}</p>
      <div className="h-1.5 rounded-full bg-outline/60 dark:bg-outline-dark/60 overflow-hidden">
        <div
          className="h-full rounded-full bg-emerald-500 transition-all duration-300"
          style={{ width: `${safePercent}%` }}
        />
      </div>
      <p className="text-xs text-content-muted dark:text-content-muted-dark">{resetLabel}</p>
    </div>
  );
};

export interface BalanceSectionProps {
  monthlyQuotaRemainingPercent: number | null;
  cycleRemainingState: { percentage: number | null; resetLabel: string };
  subscriptionData: GetUserSubscriptionResponse | undefined;
  locale: string;
  periodComparison?: PeriodComparison | null;
  rangeDays?: number;
}

export const BalanceSection = ({
  monthlyQuotaRemainingPercent,
  cycleRemainingState,
  subscriptionData,
  locale,
  periodComparison,
  rangeDays,
}: BalanceSectionProps) => {
  const { t } = useFrontendLanguage();

  return (
    <div className="space-y-5">
      <div className="grid grid-cols-1 xl:grid-cols-2 gap-6">
        <UsageBalanceCard
          title={t('Monthly quota remaining')}
          headline={`${clampPercent(monthlyQuotaRemainingPercent ?? 0)}% ${t('remaining')}`}
          progressPercent={monthlyQuotaRemainingPercent}
          resetLabel={t('Resets {date}', {
            date: subscriptionData?.currentPeriodEnd
              ? formatDateTime(subscriptionData.currentPeriodEnd, locale)
              : t('No reset date'),
          })}
        />
        <UsageBalanceCard
          title={t('Billing cycle remaining')}
          headline={`${clampPercent(cycleRemainingState.percentage ?? 0)}% ${t('remaining')}`}
          progressPercent={cycleRemainingState.percentage}
          resetLabel={cycleRemainingState.resetLabel}
        />
      </div>

      {periodComparison != null && (
        <div className="flex items-center gap-2 text-sm">
          {periodComparison.direction === 'up' ? (
            <TrendingUp className="h-4 w-4 text-red-500 dark:text-red-400" />
          ) : periodComparison.direction === 'down' ? (
            <TrendingDown className="h-4 w-4 text-emerald-500 dark:text-emerald-400" />
          ) : null}
          <span
            className={
              periodComparison.direction === 'up'
                ? 'font-medium text-red-600 dark:text-red-400'
                : periodComparison.direction === 'down'
                  ? 'font-medium text-emerald-600 dark:text-emerald-400'
                  : 'font-medium text-content-secondary dark:text-content-secondary-dark'
            }
          >
            {periodComparison.direction === 'flat'
              ? t('No change vs previous {days} days', { days: String(rangeDays ?? 30) })
              : t('{percent}% {direction} vs previous {days} days', {
                  percent: String(periodComparison.percentChange),
                  direction: periodComparison.direction === 'up' ? '↑' : '↓',
                  days: String(rangeDays ?? 30),
                })}
          </span>
        </div>
      )}
    </div>
  );
};
