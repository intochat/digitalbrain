import { useMemo, useState } from 'react';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { useFrontendLanguage } from 'app/providers';
import { useSubscriptionQuery } from 'entities/payment/api';
import { usePrivacyModeQuery } from 'entities/preferences/api';
import type { UsageSourceType } from 'entities/usage/api';
import { usageApi } from 'entities/usage/api';
import type { GetUserSubscriptionResponse } from 'shared/api';
import type { RangeOption } from './usageConstants';
import { SOURCE_ORDER } from './usageConstants';
import type { DayTimelinePoint } from './usageUtils';
import {
  buildTimelineWithEmptyDays,
  buildUtcDateRange,
  createSourceTotals,
  formatDateTime,
  resolveLocale,
} from './usageUtils';

export interface PeriodComparison {
  percentChange: number;
  direction: 'up' | 'down' | 'flat';
}

export interface UseUsageDataResult {
  isLoading: boolean;
  isError: boolean;
  monthlyQuotaRemainingPercent: number | null;
  cycleRemainingState: { percentage: number | null; resetLabel: string };
  chartTimeline: DayTimelinePoint[];
  maxTimelineValue: number;
  timelineTotals: { tokens: number; events: number };
  sourceTotals: Record<UsageSourceType, number>;
  totalTokensInRange: number;
  activeSources: UsageSourceType[];
  hasSourceBreakdownData: boolean;
  hasSourceBreakdownErrors: boolean;
  noTraceEnabled: boolean;
  isPaidUser: boolean;
  subscriptionData: GetUserSubscriptionResponse | undefined;
  rangeDays: RangeOption;
  setRangeDays: (days: RangeOption) => void;
  fromDate: string;
  toDate: string;
  locale: string;
  periodComparison: PeriodComparison | null;
}

export const useUsageData = (): UseUsageDataResult => {
  const { t, language } = useFrontendLanguage();
  const locale = resolveLocale(language);
  const [rangeDays, setRangeDays] = useState<RangeOption>(30);

  const dateRange = useMemo(() => buildUtcDateRange(rangeDays), [rangeDays]);
  const fromDate = dateRange.from;
  const toDate = dateRange.to;

  const prevDateRange = useMemo(() => {
    const from = new Date(`${fromDate}T00:00:00Z`);
    from.setUTCDate(from.getUTCDate() - rangeDays);
    const prevEnd = new Date(`${fromDate}T00:00:00Z`);
    prevEnd.setUTCDate(prevEnd.getUTCDate() - 1);
    const fmt = (d: Date) => {
      const y = d.getUTCFullYear();
      const m = String(d.getUTCMonth() + 1).padStart(2, '0');
      const dd = String(d.getUTCDate()).padStart(2, '0');
      return `${y}-${m}-${dd}`;
    };
    return { from: fmt(from), to: fmt(prevEnd) };
  }, [fromDate, rangeDays]);
  const prevFrom = prevDateRange.from;
  const prevTo = prevDateRange.to;

  const subscriptionQuery = useSubscriptionQuery();
  const privacyModeQuery = usePrivacyModeQuery();

  const isPaidUser =
    !subscriptionQuery.isLoading &&
    !subscriptionQuery.isError &&
    Boolean(subscriptionQuery.data) &&
    subscriptionQuery.data!.tierType?.toLowerCase() !== 'basic';
  const noTraceEnabled = isPaidUser && !privacyModeQuery.isError && (privacyModeQuery.data?.enabled ?? false);

  const mainQueryParams = { from: fromDate, to: toDate, groupBy: 'day' as const, page: 1, pageSize: 100 };
  const mainQuery = useQuery({
    queryKey: ['usage-events', mainQueryParams],
    queryFn: () => usageApi.getUsageEvents(mainQueryParams),
    staleTime: 60_000,
    placeholderData: keepPreviousData,
  });

  const mainPagination = mainQuery.data?.pagination;
  const needsExtraFetch = (mainPagination?.totalCount ?? 0) > (mainPagination?.pageSize ?? 100);

  const extraQueryParams = { from: fromDate, to: toDate, groupBy: 'day' as const, page: 1, pageSize: 100 };
  const extraQuery = useQuery({
    queryKey: ['usage-events', extraQueryParams],
    queryFn: () => usageApi.getUsageEvents(extraQueryParams),
    staleTime: 60_000,
    placeholderData: keepPreviousData,
    enabled: needsExtraFetch,
  });

  const prevQueryParams = { from: prevFrom, to: prevTo, groupBy: 'day' as const, page: 1, pageSize: 1 };
  const prevPeriodQuery = useQuery({
    queryKey: ['usage-events-prev', prevQueryParams],
    queryFn: () => usageApi.getUsageEvents(prevQueryParams),
    staleTime: 60_000,
    placeholderData: keepPreviousData,
  });

  const events = useMemo(
    () =>
      needsExtraFetch ? (extraQuery.data?.events ?? mainQuery.data?.events ?? []) : (mainQuery.data?.events ?? []),
    [needsExtraFetch, extraQuery.data?.events, mainQuery.data?.events]
  );
  const hasSourceBreakdownErrors = needsExtraFetch && extraQuery.isError;

  const summary = mainQuery.data?.summary;
  const monthlyLimit = summary?.monthlyLimit ?? 0;
  const remainingTokens = summary?.remainingTokens ?? 0;
  const monthlyQuotaRemainingPercent = monthlyLimit > 0 ? (remainingTokens / monthlyLimit) * 100 : null;

  const cycleRemainingState = useMemo(() => {
    const startRaw = subscriptionQuery.data?.currentPeriodStart;
    const endRaw = subscriptionQuery.data?.currentPeriodEnd;
    if (!startRaw || !endRaw) {
      return { percentage: null as number | null, resetLabel: t('No reset date') };
    }

    const start = new Date(startRaw).getTime();
    const end = new Date(endRaw).getTime();
    if (!Number.isFinite(start) || !Number.isFinite(end) || end <= start) {
      return { percentage: null as number | null, resetLabel: t('No reset date') };
    }

    const now = Date.now();
    const remainingRatio = (end - now) / (end - start);
    return {
      percentage: remainingRatio * 100,
      resetLabel: t('Resets {date}', { date: formatDateTime(endRaw, locale) }),
    };
  }, [locale, subscriptionQuery.data?.currentPeriodEnd, subscriptionQuery.data?.currentPeriodStart, t]);

  const normalizedTimeline = useMemo(
    () => buildTimelineWithEmptyDays(fromDate, toDate, mainQuery.data?.timeline ?? []),
    [fromDate, mainQuery.data?.timeline, toDate]
  );

  const sourceTokenMap = useMemo(() => {
    const map: Record<UsageSourceType, Map<string, number>> = {
      api: new Map(),
      scheduled: new Map(),
      telegram: new Map(),
      ai: new Map(),
    };

    for (const event of events) {
      const source = event.source as UsageSourceType;
      if (!map[source]) continue;
      const dateKey = event.occurredAt.slice(0, 10);
      const current = map[source].get(dateKey) ?? 0;
      map[source].set(dateKey, current + event.tokensConsumed);
    }

    return map;
  }, [events]);

  const chartTimeline = useMemo<DayTimelinePoint[]>(
    () =>
      normalizedTimeline.map(point => {
        const rawBreakdown = SOURCE_ORDER.map(source => ({
          source,
          tokens: sourceTokenMap[source].get(point.date) ?? 0,
        }));

        const sourceTotal = rawBreakdown.reduce((sum, item) => sum + item.tokens, 0);
        const totalTokens = sourceTotal > 0 ? sourceTotal : point.tokensConsumed;

        return {
          date: point.date,
          totalTokens,
          eventsCount: point.eventsCount,
          breakdown: rawBreakdown.map(item => ({
            source: item.source,
            tokens: item.tokens,
            percentage: totalTokens > 0 ? (item.tokens / totalTokens) * 100 : 0,
          })),
        };
      }),
    [normalizedTimeline, sourceTokenMap]
  );

  const sourceTotals = useMemo(() => {
    const totals = createSourceTotals();
    for (const point of chartTimeline) {
      for (const item of point.breakdown) {
        totals[item.source] += item.tokens;
      }
    }
    return totals;
  }, [chartTimeline]);

  const totalTokensInRange = useMemo(
    () => chartTimeline.reduce((sum, point) => sum + point.totalTokens, 0),
    [chartTimeline]
  );

  const activeSources = SOURCE_ORDER.filter(source => sourceTotals[source] > 0);
  const hasSourceBreakdownData = activeSources.length > 0;

  const maxTimelineValue = useMemo(
    () => chartTimeline.reduce((max, point) => Math.max(max, point.totalTokens), 0),
    [chartTimeline]
  );

  const timelineTotals = useMemo(
    () =>
      chartTimeline.reduce(
        (acc, point) => ({ tokens: acc.tokens + point.totalTokens, events: acc.events + point.eventsCount }),
        { tokens: 0, events: 0 }
      ),
    [chartTimeline]
  );

  const isLoading = mainQuery.isLoading;
  const isError = mainQuery.isError;

  const periodComparison = useMemo<PeriodComparison | null>(() => {
    if (prevPeriodQuery.isLoading || prevPeriodQuery.isError || !prevPeriodQuery.data) return null;
    const prevTimeline = prevPeriodQuery.data.timeline ?? [];
    if (prevTimeline.length === 0) return null;
    const prevTotal = prevTimeline.reduce((sum, p) => sum + p.tokensConsumed, 0);
    if (prevTotal === 0 && totalTokensInRange === 0) return { percentChange: 0, direction: 'flat' as const };
    if (prevTotal === 0) return null;
    const change = ((totalTokensInRange - prevTotal) / prevTotal) * 100;
    const rounded = Math.round(Math.abs(change));
    if (rounded === 0) return { percentChange: 0, direction: 'flat' as const };
    return { percentChange: rounded, direction: change > 0 ? 'up' : 'down' };
  }, [prevPeriodQuery.isLoading, prevPeriodQuery.isError, prevPeriodQuery.data, totalTokensInRange]);

  return {
    isLoading,
    isError,
    monthlyQuotaRemainingPercent,
    cycleRemainingState,
    chartTimeline,
    maxTimelineValue,
    timelineTotals,
    sourceTotals,
    totalTokensInRange,
    activeSources,
    hasSourceBreakdownData,
    hasSourceBreakdownErrors,
    noTraceEnabled,
    isPaidUser,
    subscriptionData: subscriptionQuery.data,
    rangeDays,
    setRangeDays,
    fromDate,
    toDate,
    locale,
    periodComparison,
  };
};
