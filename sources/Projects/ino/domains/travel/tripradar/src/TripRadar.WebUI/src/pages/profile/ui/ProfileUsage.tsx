import { ProfileLayout } from './ProfileLayout';
import { BalanceSection } from './usage/BalanceSection';
import { ChartSection } from './usage/ChartSection';
import { EventsTable } from './usage/EventsTable';
import { NoTraceBanner } from './usage/NoTraceBanner';
import { UsageBalanceSkeleton, UsageChartSkeleton } from './usage/UsageSkeletons';
import { useUsageData } from './usage/useUsageData';

export const ProfileUsage = () => {
  const data = useUsageData();

  return (
    <ProfileLayout>
      <div className="px-4 sm:px-6 lg:px-8 pb-4 sm:pb-6 lg:pb-8">
        <div>
          <NoTraceBanner visible={data.noTraceEnabled} />

          {data.isLoading ? (
            <>
              <div className="pb-8 border-b border-outline/40 dark:border-outline-dark/40">
                <UsageBalanceSkeleton />
              </div>
              <div className="pt-8">
                <UsageChartSkeleton />
              </div>
            </>
          ) : (
            <>
              <div className="pb-8 border-b border-outline/40 dark:border-outline-dark/40">
                <BalanceSection
                  monthlyQuotaRemainingPercent={data.monthlyQuotaRemainingPercent}
                  cycleRemainingState={data.cycleRemainingState}
                  subscriptionData={data.subscriptionData}
                  locale={data.locale}
                  periodComparison={data.periodComparison}
                  rangeDays={data.rangeDays}
                />
              </div>
              <div className="space-y-10 pt-8">
                <ChartSection
                  chartTimeline={data.chartTimeline}
                  maxTimelineValue={data.maxTimelineValue}
                  timelineTotals={data.timelineTotals}
                  sourceTotals={data.sourceTotals}
                  totalTokensInRange={data.totalTokensInRange}
                  activeSources={data.activeSources}
                  hasSourceBreakdownData={data.hasSourceBreakdownData}
                  hasSourceBreakdownErrors={data.hasSourceBreakdownErrors}
                  rangeDays={data.rangeDays}
                  onRangeDaysChange={data.setRangeDays}
                  fromDate={data.fromDate}
                  toDate={data.toDate}
                  locale={data.locale}
                />
                {data.maxTimelineValue > 0 && (
                  <EventsTable fromDate={data.fromDate} toDate={data.toDate} locale={data.locale} />
                )}
              </div>
            </>
          )}
        </div>
      </div>
    </ProfileLayout>
  );
};
