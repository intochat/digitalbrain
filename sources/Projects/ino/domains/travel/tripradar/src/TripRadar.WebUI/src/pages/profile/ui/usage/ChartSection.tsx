import { useState } from 'react';
import { BarChart3, Download } from 'lucide-react';
import { useFrontendLanguage } from 'app/providers';
import type { UsageSourceType } from 'entities/usage/api';
import { SectionEmpty } from 'shared/ui';
import { ChartBar } from './ChartBar';
import { ChartLegend } from './ChartLegend';
import { ChartTooltip } from './ChartTooltip';
import { downloadCsv } from './csvExport';
import { RangeSelector } from './RangeSelector';
import type { RangeOption } from './usageConstants';
import type { DayTimelinePoint } from './usageUtils';
import { formatDate } from './usageUtils';

export interface ChartSectionProps {
  chartTimeline: DayTimelinePoint[];
  maxTimelineValue: number;
  timelineTotals: { tokens: number; events: number };
  sourceTotals: Record<UsageSourceType, number>;
  totalTokensInRange: number;
  activeSources: UsageSourceType[];
  hasSourceBreakdownData: boolean;
  hasSourceBreakdownErrors: boolean;
  rangeDays: RangeOption;
  onRangeDaysChange: (days: RangeOption) => void;
  fromDate: string;
  toDate: string;
  locale: string;
}

export const ChartSection = ({
  chartTimeline,
  maxTimelineValue,
  timelineTotals,
  sourceTotals,
  totalTokensInRange,
  activeSources,
  hasSourceBreakdownData,
  hasSourceBreakdownErrors,
  rangeDays,
  onRangeDaysChange,
  fromDate,
  toDate,
  locale,
}: ChartSectionProps) => {
  const { t } = useFrontendLanguage();
  const [hoveredDate, setHoveredDate] = useState<string | null>(null);
  const hasTimelineUsage = maxTimelineValue > 0;
  const totalBars = chartTimeline.length;
  const dateLabelInterval = Math.ceil(totalBars / 8);

  return (
    <div className="space-y-5">
      {/* Header: title + range selector + export */}
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h3 className="text-sm font-medium text-content-secondary dark:text-content-secondary-dark">
          {t('Usage breakdown')}
        </h3>
        <div className="flex items-center gap-2">
          <RangeSelector value={rangeDays} onChange={onRangeDaysChange} />
          {hasTimelineUsage && (
            <button
              type="button"
              onClick={() => downloadCsv(chartTimeline, fromDate, toDate)}
              className="inline-flex items-center gap-1.5 px-2.5 py-1.5 text-xs text-content-muted dark:text-content-muted-dark hover:text-content dark:hover:text-content-dark transition-colors touch-manipulation"
            >
              <Download className="h-3.5 w-3.5" />
              <span className="hidden sm:inline">{t('Export CSV')}</span>
            </button>
          )}
        </div>
      </div>

      {hasTimelineUsage ? (
        <div className="space-y-4">
          {/* Chart area */}
          <div className="h-[16rem] relative rounded-lg border border-outline/50 dark:border-outline-dark/50 p-4">
            <div className="h-full flex items-end gap-[2px] sm:gap-1">
              {chartTimeline.map((point, index) => {
                const isHovered = hoveredDate === point.date;
                return (
                  <div key={point.date} className="relative flex-1 min-w-[4px] h-full flex items-end">
                    <ChartBar
                      point={point}
                      maxValue={maxTimelineValue}
                      index={index}
                      totalBars={totalBars}
                      locale={locale}
                      isHovered={isHovered}
                      onHover={setHoveredDate}
                    />
                    {isHovered && point.totalTokens > 0 && (
                      <ChartTooltip point={point} index={index} totalBars={totalBars} locale={locale} />
                    )}
                  </div>
                );
              })}
            </div>
          </div>

          {/* Date axis */}
          <div className="flex items-center justify-between text-xs text-content-muted dark:text-content-muted-dark">
            <span>{formatDate(fromDate, locale)}</span>
            <div className="hidden sm:flex items-center justify-between flex-1 px-2">
              {chartTimeline
                .slice(1, -1)
                .map((point, i) =>
                  (i + 1) % dateLabelInterval === 0 ? (
                    <span key={point.date}>{formatDate(point.date, locale)}</span>
                  ) : null
                )}
            </div>
            <span>{formatDate(toDate, locale)}</span>
          </div>

          <ChartLegend
            activeSources={activeSources}
            sourceTotals={sourceTotals}
            totalTokensInRange={totalTokensInRange}
            hasSourceBreakdownData={hasSourceBreakdownData}
          />

          {hasSourceBreakdownErrors && (
            <p className="text-center text-xs text-amber-700 dark:text-amber-300">
              {t('Source split is partially unavailable.')}
            </p>
          )}

          {/* Totals */}
          <p className="text-center text-xs text-content-muted dark:text-content-muted-dark">
            {timelineTotals.tokens.toLocaleString(locale)} {t('tokens')} ·{' '}
            {timelineTotals.events.toLocaleString(locale)} {t('events')}
          </p>
        </div>
      ) : (
        <SectionEmpty
          message={t('No usage detected in this period.') + '\n' + t('Try selecting a different date range.')}
          icon={<BarChart3 />}
        />
      )}
    </div>
  );
};
