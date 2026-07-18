import { useFrontendLanguage } from 'app/providers';
import type { UsageSourceType } from 'entities/usage/api';
import { SOURCE_META } from './usageConstants';
import { clampPercent } from './usageUtils';

export interface ChartLegendProps {
  activeSources: UsageSourceType[];
  sourceTotals: Record<UsageSourceType, number>;
  totalTokensInRange: number;
  hasSourceBreakdownData: boolean;
}

export const ChartLegend = ({
  activeSources,
  sourceTotals,
  totalTokensInRange,
  hasSourceBreakdownData,
}: ChartLegendProps) => {
  const { t } = useFrontendLanguage();
  const sources = hasSourceBreakdownData ? activeSources : (['api'] as UsageSourceType[]);

  return (
    <div className="flex flex-wrap items-center justify-center gap-x-5 gap-y-2 text-sm text-content dark:text-content-dark">
      {sources.map(source => {
        const sourcePercent = totalTokensInRange > 0 ? (sourceTotals[source] / totalTokensInRange) * 100 : 0;

        return (
          <div key={source} className="inline-flex items-center gap-2">
            <span className={`h-2.5 w-2.5 rounded-sm ${SOURCE_META[source].dotClass}`} />
            <span>{hasSourceBreakdownData ? t(SOURCE_META[source].labelKey) : t('Usage events')}</span>
            {hasSourceBreakdownData && (
              <span className="font-semibold text-content-secondary dark:text-content-secondary-dark">
                {clampPercent(sourcePercent)}%
              </span>
            )}
          </div>
        );
      })}
    </div>
  );
};
