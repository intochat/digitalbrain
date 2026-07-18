import { useState } from 'react';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { BarChart3, ChevronLeft, ChevronRight } from 'lucide-react';
import { useFrontendLanguage } from 'app/providers';
import { usageApi } from 'entities/usage/api';
import type { UsageEventItemResponse } from 'entities/usage/api';
import { SectionEmpty, SectionError } from 'shared/ui';
import { EventsTableSkeleton } from './EventsTableSkeleton';
import { formatDateTime, formatUsageServiceType } from './usageUtils';

const PAGE_SIZE = 10;

interface EventsTableProps {
  fromDate: string;
  toDate: string;
  locale: string;
}

export const EventsTable = ({ fromDate, toDate, locale }: EventsTableProps) => {
  const { t } = useFrontendLanguage();
  const [page, setPage] = useState(1);

  const queryParams = { from: fromDate, to: toDate, page, pageSize: PAGE_SIZE };
  const { data, isLoading, isError, refetch } = useQuery({
    queryKey: ['usage-events-table', queryParams],
    queryFn: () => usageApi.getUsageEvents(queryParams),
    staleTime: 60_000,
    placeholderData: keepPreviousData,
  });

  const events = data?.events ?? [];
  const pagination = data?.pagination;
  const totalPages = pagination?.totalPages ?? 1;

  if (isLoading && events.length === 0) {
    return <EventsTableSkeleton />;
  }

  if (isError) {
    return <SectionError message={t('Unable to load usage timeline.')} onRetry={() => refetch()} />;
  }

  if (events.length === 0) {
    return (
      <SectionEmpty
        message={t('No usage detected in this period.')}
        icon={<BarChart3 className="h-6 w-6 text-content-muted dark:text-content-muted-dark" />}
      />
    );
  }

  return (
    <div className="space-y-4">
      <h3 className="text-sm font-medium text-content-secondary dark:text-content-secondary-dark">
        {t('Usage events')}
      </h3>

      <div className="hidden sm:block">
        <table className="w-full text-sm text-left">
          <thead>
            <tr className="border-b border-outline dark:border-outline-dark">
              <th className="pb-2 pr-6 text-xs font-medium text-content-muted dark:text-content-muted-dark whitespace-nowrap">
                {t('Date/Time')}
              </th>
              <th className="pb-2 pr-6 text-xs font-medium text-content-muted dark:text-content-muted-dark">
                {t('Service type')}
              </th>
              <th className="pb-2 text-xs font-medium text-content-muted dark:text-content-muted-dark whitespace-nowrap">
                {t('Tokens')}
              </th>
            </tr>
          </thead>
          <tbody>
            {events.map(event => (
              <EventRow key={event.uniqueId} event={event} locale={locale} t={t} />
            ))}
          </tbody>
        </table>
      </div>

      <div className="sm:hidden space-y-0">
        {events.map(event => (
          <EventRowMobile key={event.uniqueId} event={event} locale={locale} t={t} />
        ))}
      </div>

      {totalPages > 1 && (
        <div className="flex items-center justify-between pt-2">
          <p className="text-xs text-content-muted dark:text-content-muted-dark">
            {t('Page {page} of {total}', { page: String(page), total: String(totalPages) })}
          </p>
          <div className="flex items-center gap-1">
            <button
              type="button"
              disabled={page <= 1}
              onClick={() => setPage(p => Math.max(1, p - 1))}
              aria-label={t('Previous page')}
              className="rounded-md p-1 text-content-muted dark:text-content-muted-dark hover:text-content dark:hover:text-content-dark disabled:opacity-40 disabled:pointer-events-none transition-colors"
            >
              <ChevronLeft className="h-4 w-4" />
            </button>
            <button
              type="button"
              disabled={page >= totalPages}
              onClick={() => setPage(p => Math.min(totalPages, p + 1))}
              aria-label={t('Next page')}
              className="rounded-md p-1 text-content-muted dark:text-content-muted-dark hover:text-content dark:hover:text-content-dark disabled:opacity-40 disabled:pointer-events-none transition-colors"
            >
              <ChevronRight className="h-4 w-4" />
            </button>
          </div>
        </div>
      )}
    </div>
  );
};

interface EventRowProps {
  event: UsageEventItemResponse;
  locale: string;
  t: (key: string, params?: Record<string, string>) => string;
}

const EventRow = ({ event, locale, t }: EventRowProps) => {
  return (
    <tr className="border-b border-outline/50 dark:border-outline-dark/50 last:border-b-0">
      <td className="py-2.5 text-content dark:text-content-dark whitespace-nowrap">
        {formatDateTime(event.occurredAt, locale)}
      </td>
      <td className="py-2.5 text-content dark:text-content-dark">{formatUsageServiceType(event.serviceType, t)}</td>
      <td className="py-2.5 tabular-nums text-content dark:text-content-dark">
        {event.tokensConsumed.toLocaleString(locale)}
      </td>
    </tr>
  );
};

const EventRowMobile = ({ event, locale, t }: EventRowProps) => {
  return (
    <div className="py-2.5 border-b border-outline/50 dark:border-outline-dark/50 last:border-b-0">
      <div className="flex items-center justify-between mb-0.5">
        <span className="text-xs text-content-muted dark:text-content-muted-dark">
          {formatDateTime(event.occurredAt, locale)}
        </span>
        <span className="text-sm tabular-nums text-content dark:text-content-dark">
          {event.tokensConsumed.toLocaleString(locale)}
        </span>
      </div>
      <div className="text-sm text-content dark:text-content-dark">{formatUsageServiceType(event.serviceType, t)}</div>
    </div>
  );
};
