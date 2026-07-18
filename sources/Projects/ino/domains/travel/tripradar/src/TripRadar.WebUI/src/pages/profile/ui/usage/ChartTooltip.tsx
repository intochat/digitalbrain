import { useFrontendLanguage } from 'app/providers';
import { SOURCE_META } from './usageConstants';
import type { DayTimelinePoint } from './usageUtils';
import { clampPercent, formatTooltipDate } from './usageUtils';

export interface ChartTooltipProps {
  point: DayTimelinePoint;
  index: number;
  totalBars: number;
  locale: string;
}

export const ChartTooltip = ({ point, index, totalBars, locale }: ChartTooltipProps) => {
  const { t } = useFrontendLanguage();
  const activeBreakdown = point.breakdown.filter(item => item.tokens > 0);

  const positionClass = index >= totalBars - 4 ? 'right-0' : index <= 3 ? 'left-0' : 'left-1/2 -translate-x-1/2';

  return (
    <div
      className={`pointer-events-none absolute top-2 z-20 w-56 rounded-xl border border-outline dark:border-outline-dark bg-surface dark:bg-surface-dark px-3 py-2 shadow-xl ${positionClass}`}
    >
      <p className="text-sm font-semibold text-content dark:text-content-dark">
        {formatTooltipDate(point.date, locale)}
      </p>
      <p className="mt-1 text-xs text-content-secondary dark:text-content-secondary-dark">
        {t('Total for day')}: {point.totalTokens.toLocaleString(locale)} {t('tokens')}
      </p>
      <div className="mt-2 space-y-1.5">
        {activeBreakdown.length > 0 ? (
          activeBreakdown.map(item => (
            <div key={item.source} className="flex items-center gap-2 text-xs text-content dark:text-content-dark">
              <span className={`h-2.5 w-2.5 rounded-sm ${SOURCE_META[item.source].dotClass}`} />
              <span className="truncate">{t(SOURCE_META[item.source].labelKey)}</span>
              <span className="ml-auto font-semibold">{clampPercent(item.percentage)}%</span>
            </div>
          ))
        ) : (
          <div className="flex items-center gap-2 text-xs text-content dark:text-content-dark">
            <span className="h-2.5 w-2.5 rounded-sm bg-red-500 dark:bg-red-400" />
            <span>{t('Usage events')}</span>
            <span className="ml-auto font-semibold">100%</span>
          </div>
        )}
      </div>
    </div>
  );
};
