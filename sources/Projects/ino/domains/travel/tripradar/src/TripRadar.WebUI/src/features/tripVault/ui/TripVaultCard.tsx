import { useState } from 'react';
import { CheckCircle2, ExternalLink, Pencil, Trash2 } from 'lucide-react';
import { useFrontendLanguage } from 'app/providers';
import type { TripVaultItem } from 'entities/tripVault';
import { Button } from 'shared/ui';
import { formatDate, formatDateTime } from './tripVaultUtils';

type ActivityState = 'active' | 'scheduled' | 'expired';

interface TripVaultCardProps {
  trip: TripVaultItem;
  activityState: ActivityState;
  isActiveForSearch: boolean;
  isAnyMutationPending: boolean;
  onSetDefault: () => void;
  onOpenHistory: () => void;
  onEdit: () => void;
  onDelete: () => Promise<void>;
}

const STATUS_BADGE: Record<ActivityState, string> = {
  active: 'bg-emerald-100 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300',
  scheduled: 'bg-amber-100 text-amber-800 dark:bg-amber-500/15 dark:text-amber-300',
  expired: 'bg-gray-100 text-gray-600 dark:bg-gray-500/15 dark:text-gray-400',
};

const STATUS_DOT: Record<ActivityState, string> = {
  active: 'bg-emerald-500',
  scheduled: 'bg-amber-500',
  expired: 'bg-gray-400 dark:bg-gray-500',
};

const STATUS_LABELS: Record<ActivityState, string> = {
  active: 'Active now',
  scheduled: 'Not started yet',
  expired: 'Inactive',
};

export const TripVaultCard = ({
  trip,
  activityState,
  isActiveForSearch,
  isAnyMutationPending,
  onSetDefault,
  onOpenHistory,
  onEdit,
  onDelete,
}: TripVaultCardProps) => {
  const { t } = useFrontendLanguage();
  const isActive = activityState === 'active';
  const [confirmingDelete, setConfirmingDelete] = useState(false);
  const [isDeleting, setIsDeleting] = useState(false);

  const handleConfirmDelete = async () => {
    setIsDeleting(true);
    try {
      await onDelete();
    } finally {
      setIsDeleting(false);
      setConfirmingDelete(false);
    }
  };

  return (
    <article className="rounded-lg border border-outline dark:border-outline-dark bg-surface dark:bg-surface-dark p-4 sm:p-5">
      <div className="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
        <div className="space-y-3 flex-1 min-w-0">
          {/* Badges row */}
          <div className="flex items-center gap-2 flex-wrap">
            {isActiveForSearch && (
              <span className="inline-flex items-center gap-1 rounded-full bg-emerald-100 dark:bg-emerald-500/15 px-2.5 py-1 text-xs font-medium text-emerald-700 dark:text-emerald-300">
                <CheckCircle2 className="h-3.5 w-3.5" />
                {t('Default for chat')}
              </span>
            )}
            <span className={`rounded-full px-2.5 py-1 text-xs font-medium ${STATUS_BADGE[activityState]}`}>
              {t(STATUS_LABELS[activityState])}
            </span>
            <span
              className={`inline-flex h-2 w-2 rounded-full ${STATUS_DOT[activityState]}`}
              aria-label={t(STATUS_LABELS[activityState])}
            />
          </div>

          {/* Title */}
          <h4 className="text-sm font-medium text-content dark:text-content-dark break-words">{trip.name}</h4>

          {/* Details */}
          {trip.description && (
            <p className="text-sm text-content-secondary dark:text-content-secondary-dark break-words">
              {trip.description}
            </p>
          )}
          <div className="flex flex-wrap items-baseline gap-x-5 gap-y-1">
            {(trip.startDate || trip.endDate) && (
              <span className="whitespace-nowrap">
                <span className="text-xs text-content-secondary dark:text-content-secondary-dark">{t('Dates')}: </span>
                <span className="text-sm text-content dark:text-content-dark">
                  {formatDate(trip.startDate)} — {formatDate(trip.endDate)}
                </span>
              </span>
            )}
            <span className="whitespace-nowrap">
              <span className="text-xs text-content-secondary dark:text-content-secondary-dark">
                {t('History items')}:{' '}
              </span>
              <span className="text-sm text-content dark:text-content-dark">{trip.itemsCount}</span>
            </span>
            <span className="whitespace-nowrap">
              <span className="text-xs text-content-secondary dark:text-content-secondary-dark">{t('Created')}: </span>
              <span className="text-sm text-content dark:text-content-dark">{formatDateTime(trip.createdOn)}</span>
            </span>
          </div>
        </div>

        {/* Actions */}
        <div className="flex items-center gap-2 self-start shrink-0 pt-1">
          {confirmingDelete ? (
            <div className="flex items-center gap-2">
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
            <>
              {!isActiveForSearch && (
                <button
                  type="button"
                  onClick={onSetDefault}
                  disabled={!isActive}
                  title={!isActive ? t('Vault is inactive outside selected dates.') : undefined}
                  className="rounded-lg px-3 py-1.5 text-xs font-medium text-content-secondary dark:text-content-secondary-dark hover:bg-surface-accent dark:hover:bg-surface-accent-dark disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
                >
                  {t('Set default')}
                </button>
              )}
              <button
                type="button"
                onClick={onOpenHistory}
                className="rounded-lg p-2 text-content-secondary dark:text-content-secondary-dark hover:bg-surface-accent dark:hover:bg-surface-accent-dark transition-colors"
                aria-label={t('History')}
              >
                <ExternalLink className="h-4 w-4" />
              </button>
              <button
                type="button"
                onClick={onEdit}
                disabled={isAnyMutationPending}
                className="rounded-lg p-2 text-content-secondary dark:text-content-secondary-dark hover:bg-surface-accent dark:hover:bg-surface-accent-dark disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
                aria-label={t('Edit')}
              >
                <Pencil className="h-4 w-4" />
              </button>
              <button
                type="button"
                onClick={() => setConfirmingDelete(true)}
                disabled={isAnyMutationPending}
                className="rounded-lg p-2 text-content-muted dark:text-content-muted-dark hover:text-red-500 dark:hover:text-red-400 disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
                aria-label={t('Delete')}
              >
                <Trash2 className="h-4 w-4" />
              </button>
            </>
          )}
        </div>
      </div>
    </article>
  );
};
