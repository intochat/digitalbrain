import { useState } from 'react';
import { Pencil, Trash2 } from 'lucide-react';
import type { ScheduledExecutionItem } from 'entities/scheduledRequests';
import { Switch } from 'shared/ui';
import type { ScheduledRequestFormState } from '../constants';
import {
  formatExecutionDate,
  formatExecutionDateTime,
  getExecutionCardTitle,
  getExecutionRouteLabel,
  resolveServiceBadge,
  toDisplayCityName,
} from '../utils';
import { InlineDeleteConfirmation } from './InlineDeleteConfirmation';
import { InlineEditor } from './InlineEditor';

interface RequestCardProps {
  execution: ScheduledExecutionItem;
  isEditing: boolean;
  onStartEdit: () => void;
  onCancelEdit: () => void;
  onSaveEdit: (formState: ScheduledRequestFormState) => Promise<void>;
  onToggle: () => void;
  onDelete: () => Promise<void>;
  isToggling: boolean;
  locale: string;
  t: (key: string, params?: Record<string, string | number>) => string;
}

export const RequestCard = ({
  execution,
  isEditing,
  onStartEdit,
  onCancelEdit,
  onSaveEdit,
  onToggle,
  onDelete,
  isToggling,
  locale,
  t,
}: RequestCardProps) => {
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

  if (isEditing) {
    return (
      <article className="rounded-lg border border-outline dark:border-outline-dark bg-surface dark:bg-surface-dark p-4 sm:p-5">
        <InlineEditor execution={execution} onSave={onSaveEdit} onCancel={onCancelEdit} t={t} />
      </article>
    );
  }

  const badge = resolveServiceBadge(execution.serviceType);
  const notSetLabel = t('Not set');
  const title = getExecutionCardTitle(execution);
  const nextRun = formatExecutionDateTime(execution.nextExecutionTime, locale, notSetLabel);

  const routeLabel =
    execution.departureAirportCode && execution.destinationAirportCode ? getExecutionRouteLabel(execution, t) : null;
  const stayLabel =
    execution.checkInDate && execution.checkOutDate
      ? `${formatExecutionDate(execution.checkInDate, locale, notSetLabel)} - ${formatExecutionDate(execution.checkOutDate, locale, notSetLabel)}`
      : null;
  const location = toDisplayCityName(execution.location) ?? execution.location;

  return (
    <article className="rounded-lg border border-outline dark:border-outline-dark bg-surface dark:bg-surface-dark p-4 sm:p-5">
      <div className="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
        <div className="space-y-3 flex-1 min-w-0">
          <div className="flex items-center gap-2 flex-wrap">
            <span className={`rounded-full px-2.5 py-1 text-xs font-medium ${badge.className}`}>{t(badge.label)}</span>
            <span
              className={`inline-flex h-2 w-2 rounded-full ${execution.isActive ? 'bg-emerald-500' : 'bg-gray-400 dark:bg-gray-500'}`}
              aria-label={execution.isActive ? t('Active') : t('Paused')}
            />
          </div>

          <h4 className="text-sm font-medium text-content dark:text-content-dark break-words">{title}</h4>

          <div className="grid grid-cols-1 sm:grid-cols-2 gap-2">
            <DetailRow label={t('Next Run')} value={nextRun} />
            {execution.searchQuery && <DetailRow label={t('Query')} value={execution.searchQuery} />}
            {location && <DetailRow label={t('Location')} value={location} />}
            {routeLabel && <DetailRow label={t('Route')} value={routeLabel} />}
            {stayLabel && <DetailRow label={t('Stay')} value={stayLabel} />}
          </div>
        </div>

        <div className="flex items-center gap-2 self-start shrink-0 pt-1">
          {confirmingDelete ? (
            <InlineDeleteConfirmation
              onConfirm={handleConfirmDelete}
              onCancel={() => setConfirmingDelete(false)}
              isDeleting={isDeleting}
              t={t}
            />
          ) : (
            <>
              <Switch
                checked={execution.isActive}
                onChange={onToggle}
                disabled={isToggling}
                aria-label={execution.isActive ? t('Pause request') : t('Resume request')}
              />
              <button
                type="button"
                onClick={onStartEdit}
                className="rounded-lg p-2 text-content-secondary dark:text-content-secondary-dark hover:bg-surface-accent dark:hover:bg-surface-accent-dark"
                aria-label={t('Edit')}
              >
                <Pencil className="h-4 w-4" />
              </button>
              <button
                type="button"
                onClick={() => setConfirmingDelete(true)}
                className="rounded-lg p-2 text-content-muted dark:text-content-muted-dark hover:text-red-500 dark:hover:text-red-400 transition-colors"
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

const DetailRow = ({ label, value }: { label: string; value: string }) => (
  <div className="min-w-0">
    <span className="text-xs text-content-secondary dark:text-content-secondary-dark">{label}: </span>
    <span className="text-sm text-content dark:text-content-dark">{value}</span>
  </div>
);
