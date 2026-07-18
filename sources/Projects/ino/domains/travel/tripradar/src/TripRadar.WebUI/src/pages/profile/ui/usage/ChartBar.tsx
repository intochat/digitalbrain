import { useFrontendLanguage } from 'app/providers';
import { SOURCE_META } from './usageConstants';
import type { DayTimelinePoint } from './usageUtils';
import { clampPercent, formatTooltipDate } from './usageUtils';

export interface ChartBarProps {
  point: DayTimelinePoint;
  maxValue: number;
  index: number;
  totalBars: number;
  locale: string;
  isHovered: boolean;
  onHover: (date: string | null) => void;
}

export const ChartBar = ({ point, maxValue, locale, onHover }: ChartBarProps) => {
  const { t } = useFrontendLanguage();
  const heightPercent = maxValue > 0 ? Math.max((point.totalTokens / maxValue) * 100, 8) : 0;
  const barHeight = point.totalTokens > 0 ? `${heightPercent}%` : '0%';
  const activeBreakdown = point.breakdown.filter(item => item.tokens > 0);

  return (
    <div
      className="relative flex h-full min-w-[4px] flex-1 items-end cursor-pointer"
      onMouseEnter={() => onHover(point.date)}
      onMouseLeave={() => onHover(null)}
      onFocus={() => onHover(point.date)}
      onBlur={() => onHover(null)}
      role="button"
      tabIndex={0}
      aria-label={`${formatTooltipDate(point.date, locale)}: ${point.totalTokens.toLocaleString(locale)} ${t('tokens')}`}
    >
      <div
        className="w-full overflow-hidden rounded-t-sm transition-[height] duration-[400ms] ease-[cubic-bezier(0.4,0,0.2,1)] motion-reduce:transition-none"
        style={{ height: barHeight }}
      >
        {point.totalTokens > 0 && (
          <div className="flex h-full w-full flex-col-reverse">
            {activeBreakdown.length > 0 ? (
              activeBreakdown.map(item => (
                <div
                  key={item.source}
                  className={SOURCE_META[item.source].segmentClass}
                  style={{ height: `${clampPercent((item.tokens / point.totalTokens) * 100)}%` }}
                />
              ))
            ) : (
              <div className="h-full w-full bg-red-500/90 dark:bg-red-400/90" />
            )}
          </div>
        )}
      </div>
    </div>
  );
};
