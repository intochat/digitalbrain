import { useState } from 'react';
import { ChevronLeft, ChevronRight, Clock3, Trash2 } from 'lucide-react';
import { useFrontendLanguage } from 'app/providers';
import type { TripHistoryItem } from 'entities/tripVault';
import { HistoryItemCard } from 'features/tripVault/ui/historyCards';
import { Button, SectionEmpty, SectionError } from 'shared/ui';
import { HistoryListSkeleton } from './HistoryListSkeleton';
import { formatDateTime, humanizeServiceType, toServiceBadgeClass } from './tripHistoryUtils';

interface PaginatedData {
  items: TripHistoryItem[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

interface HistoryListProps {
  tripUniqueId: string | null;
  isLoading: boolean;
  isError: boolean;
  data: PaginatedData | undefined;
  onDelete: (item: TripHistoryItem) => Promise<void>;
  onPageChange: (page: number) => void;
  onRefresh: () => void;
}

interface HistoryItemRowProps {
  item: TripHistoryItem;
  onDelete: (item: TripHistoryItem) => Promise<void>;
}

const HistoryItemRow = ({ item, onDelete }: HistoryItemRowProps) => {
  const { t } = useFrontendLanguage();
  const [confirmingDelete, setConfirmingDelete] = useState(false);
  const [isDeleting, setIsDeleting] = useState(false);

  const handleConfirmDelete = async () => {
    setIsDeleting(true);
    try {
      await onDelete(item);
    } finally {
      setIsDeleting(false);
      setConfirmingDelete(false);
    }
  };

  return (
    <article className="rounded-lg border border-outline dark:border-outline-dark bg-surface dark:bg-surface-dark p-4">
      <div className="space-y-3">
        <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
          <div className="flex items-center gap-2 flex-wrap">
            <span
              className={`rounded-full px-2.5 py-0.5 text-[11px] font-medium uppercase tracking-wide ${toServiceBadgeClass(item.serviceType)}`}
            >
              {t(humanizeServiceType(item.serviceType))}
            </span>
            <span className="text-[11px] text-content-secondary dark:text-content-secondary-dark">
              {formatDateTime(item.createdOn)}
            </span>
            {(item.startDateTime || item.endDateTime) && (
              <span className="text-[11px] text-content-secondary dark:text-content-secondary-dark">
                {item.startDateTime && t('From {date}', { date: formatDateTime(item.startDateTime) })}
                {item.startDateTime && item.endDateTime && ' — '}
                {item.endDateTime && t('Until {date}', { date: formatDateTime(item.endDateTime) })}
              </span>
            )}
          </div>
          {confirmingDelete ? (
            <div className="flex items-center gap-2 self-end sm:self-auto">
              <span className="text-sm text-content-secondary dark:text-content-secondary-dark">{t('Delete?')}</span>
              <Button
                variant="ghost"
                size="sm"
                onClick={handleConfirmDelete}
                disabled={isDeleting}
                isLoading={isDeleting}
              >
                {t('Yes')}
              </Button>
              <Button variant="ghost" size="sm" onClick={() => setConfirmingDelete(false)} disabled={isDeleting}>
                {t('No')}
              </Button>
            </div>
          ) : (
            <button
              type="button"
              onClick={() => setConfirmingDelete(true)}
              className="rounded-lg p-2 text-content-muted dark:text-content-muted-dark hover:text-red-500 dark:hover:text-red-400 transition-colors self-end sm:self-auto"
              aria-label={t('Delete')}
            >
              <Trash2 className="h-4 w-4" />
            </button>
          )}
        </div>
        <HistoryItemCard item={item} />
      </div>
    </article>
  );
};

export const HistoryList = ({
  tripUniqueId,
  isLoading,
  isError,
  data,
  onDelete,
  onPageChange,
  onRefresh,
}: HistoryListProps) => {
  const { t } = useFrontendLanguage();

  if (!tripUniqueId) {
    return (
      <SectionEmpty
        message={t('Trip was not specified')}
        icon={<Clock3 className="h-6 w-6 text-content-muted dark:text-content-muted-dark" />}
      />
    );
  }

  if (isLoading) {
    return <HistoryListSkeleton />;
  }

  if (isError) {
    return <SectionError message={t('Unable to load query history')} onRetry={onRefresh} />;
  }

  if (!data || data.items.length === 0) {
    const isFilteredEmpty = data && data.totalCount > 0 && data.items.length === 0;
    return (
      <SectionEmpty
        message={isFilteredEmpty ? t('No items match this filter') : t('No history items yet')}
        icon={<Clock3 className="h-6 w-6 text-content-muted dark:text-content-muted-dark" />}
      />
    );
  }

  return (
    <div className="space-y-3">
      {data.items.map((item, index) => (
        <HistoryItemRow key={`${item.uniqueId}-${item.createdOn}-${index}`} item={item} onDelete={onDelete} />
      ))}

      {data.totalPages > 1 && (
        <div className="flex items-center justify-between pt-2">
          <span className="text-[11px] text-content-muted dark:text-content-muted-dark tabular-nums">
            {(data.page - 1) * data.pageSize + 1}–{Math.min(data.page * data.pageSize, data.totalCount)} of{' '}
            {data.totalCount}
          </span>
          <div className="flex items-center gap-1">
            <button
              type="button"
              onClick={() => onPageChange(Math.max(1, data.page - 1))}
              disabled={data.page <= 1}
              className="p-1 rounded-md text-content-muted dark:text-content-muted-dark hover:text-content dark:hover:text-content-dark hover:bg-surface-accent dark:hover:bg-surface-accent-dark disabled:opacity-30 disabled:cursor-not-allowed transition-colors"
              aria-label={t('Previous page')}
            >
              <ChevronLeft className="h-3.5 w-3.5" />
            </button>
            <div className="flex items-center gap-1 px-1">
              {Array.from({ length: data.totalPages }, (_, i) => (
                <button
                  key={i}
                  type="button"
                  onClick={() => onPageChange(i + 1)}
                  className={`h-1.5 rounded-full transition-all ${
                    i + 1 === data.page
                      ? 'w-4 bg-content dark:bg-content-dark'
                      : 'w-1.5 bg-content-muted/30 dark:bg-content-muted-dark/30 hover:bg-content-muted/50 dark:hover:bg-content-muted-dark/50'
                  }`}
                  aria-label={`${t('Page')} ${i + 1}`}
                />
              ))}
            </div>
            <button
              type="button"
              onClick={() => onPageChange(Math.min(data.totalPages, data.page + 1))}
              disabled={data.page >= data.totalPages}
              className="p-1 rounded-md text-content-muted dark:text-content-muted-dark hover:text-content dark:hover:text-content-dark hover:bg-surface-accent dark:hover:bg-surface-accent-dark disabled:opacity-30 disabled:cursor-not-allowed transition-colors"
              aria-label={t('Next page')}
            >
              <ChevronRight className="h-3.5 w-3.5" />
            </button>
          </div>
        </div>
      )}
    </div>
  );
};
