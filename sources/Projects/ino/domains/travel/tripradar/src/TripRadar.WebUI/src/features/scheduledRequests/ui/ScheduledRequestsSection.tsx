import { useMemo, useState } from 'react';
import { Clock3, Plus } from 'lucide-react';
import { useFrontendLanguage } from 'app/providers';
import { useToast } from 'app/providers/ToastProvider';
import {
  useDeleteScheduledExecutionMutation,
  useScheduledExecutionsQuery,
  useUpdateScheduledExecutionConfigurationMutation,
  useUpdateScheduledExecutionQueryMutation,
} from 'entities/scheduledRequests';
import { Button, SectionEmpty, SectionError } from 'shared/ui';
import { CreationForm, FilterTabs, RequestCard, RequestCardSkeleton } from './components';
import type { ScheduledRequestFormState } from './constants';
import type { FilterTabValue } from './utils';
import {
  buildUpdateQueryPayload,
  computeFilterCounts,
  filterByType,
  getExecutionCardTitle,
  getValidationError,
  resolveLocale,
  sortByNextExecution,
  toDateTimeLocalInputValue,
} from './utils';

interface ScheduledRequestsSectionProps {
  className?: string;
}

export const ScheduledRequestsSection = ({ className = '' }: ScheduledRequestsSectionProps) => {
  const { t, language } = useFrontendLanguage();
  const locale = resolveLocale(language);
  const { showError, showSuccess } = useToast();

  const [activeFilter, setActiveFilter] = useState<FilterTabValue>('all');
  const [isFormOpen, setIsFormOpen] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [togglingId, setTogglingId] = useState<string | null>(null);

  const scheduledExecutionsQuery = useScheduledExecutionsQuery();
  const updateQueryMutation = useUpdateScheduledExecutionQueryMutation();
  const updateConfigMutation = useUpdateScheduledExecutionConfigurationMutation();
  const deleteMutation = useDeleteScheduledExecutionMutation();

  const allExecutions = useMemo(
    () => sortByNextExecution(scheduledExecutionsQuery.data?.scheduledExecutions ?? []),
    [scheduledExecutionsQuery.data?.scheduledExecutions]
  );

  const counts = useMemo(() => computeFilterCounts(allExecutions), [allExecutions]);
  const filtered = useMemo(() => filterByType(allExecutions, activeFilter), [allExecutions, activeFilter]);

  const hasData = allExecutions.length > 0;

  const handleToggle = async (execution: (typeof allExecutions)[0]) => {
    setTogglingId(execution.scheduledExecutionUniqueId);
    try {
      await updateConfigMutation.mutateAsync({
        uniqueId: execution.scheduledExecutionUniqueId,
        configuration: {
          isActive: !execution.isActive,
          schedule: execution.schedule,
          nextExecutionTime: execution.nextExecutionTime,
        },
      });
      showSuccess(execution.isActive ? t('Request paused') : t('Request activated'), getExecutionCardTitle(execution));
    } catch {
      showError(t('Update failed'), t('Unable to update scheduled request state.'));
    } finally {
      setTogglingId(null);
    }
  };

  const handleSaveEdit = async (execution: (typeof allExecutions)[0], formState: ScheduledRequestFormState) => {
    const requiresFuture =
      execution.schedule !== formState.schedule ||
      toDateTimeLocalInputValue(execution.nextExecutionTime) !== formState.nextExecutionTime;
    const error = getValidationError(formState, requiresFuture);
    if (error) {
      showError(t('Invalid request'), error);
      return;
    }
    try {
      await updateQueryMutation.mutateAsync({
        uniqueId: execution.scheduledExecutionUniqueId,
        request: buildUpdateQueryPayload(formState),
      });
      await updateConfigMutation.mutateAsync({
        uniqueId: execution.scheduledExecutionUniqueId,
        configuration: {
          isActive: execution.isActive,
          schedule: formState.schedule,
          nextExecutionTime: new Date(formState.nextExecutionTime).toISOString(),
        },
      });
      await scheduledExecutionsQuery.refetch();
      showSuccess(t('Scheduled request updated'), t('Your scheduled request has been updated.'));
      setEditingId(null);
    } catch {
      showError(t('Update failed'), t('Unable to update scheduled request. Please check your input and try again.'));
    }
  };

  const handleDelete = async (execution: (typeof allExecutions)[0]) => {
    try {
      await deleteMutation.mutateAsync({ uniqueId: execution.scheduledExecutionUniqueId });
      showSuccess(t('Scheduled request deleted'), getExecutionCardTitle(execution));
    } catch {
      showError(t('Delete failed'), t('Unable to delete scheduled request.'));
    }
  };

  const handleCreated = async () => {
    await scheduledExecutionsQuery.refetch();
    setIsFormOpen(false);
  };

  if (scheduledExecutionsQuery.isLoading) {
    return (
      <div className={`space-y-3 ${className}`}>
        <RequestCardSkeleton />
        <RequestCardSkeleton />
        <RequestCardSkeleton />
      </div>
    );
  }

  if (scheduledExecutionsQuery.isError) {
    return (
      <div className={className}>
        <SectionError
          message={t('Unable to load scheduled requests')}
          onRetry={() => scheduledExecutionsQuery.refetch()}
        />
      </div>
    );
  }

  return (
    <div className={`space-y-4 ${className}`}>
      {hasData && (
        <>
          <FilterTabs activeTab={activeFilter} onTabChange={setActiveFilter} counts={counts} t={t} />
        </>
      )}

      {!hasData && !isFormOpen && (
        <SectionEmpty
          message={t('No scheduled requests yet')}
          icon={<Clock3 className="h-6 w-6 text-content-muted dark:text-content-muted-dark" />}
          action={
            <Button variant="primary" size="sm" onClick={() => setIsFormOpen(true)}>
              {t('Create your first request')}
            </Button>
          }
        />
      )}

      {hasData && (
        <div className="space-y-3">
          {filtered.map(execution => (
            <RequestCard
              key={execution.scheduledExecutionUniqueId}
              execution={execution}
              isEditing={editingId === execution.scheduledExecutionUniqueId}
              onStartEdit={() => setEditingId(execution.scheduledExecutionUniqueId)}
              onCancelEdit={() => setEditingId(null)}
              onSaveEdit={formState => handleSaveEdit(execution, formState)}
              onToggle={() => handleToggle(execution)}
              onDelete={() => handleDelete(execution)}
              isToggling={togglingId === execution.scheduledExecutionUniqueId}
              locale={locale}
              t={t}
            />
          ))}
          {filtered.length === 0 && (
            <SectionEmpty
              message={t('No requests match this filter')}
              icon={<Clock3 className="h-6 w-6 text-content-muted dark:text-content-muted-dark" />}
            />
          )}
        </div>
      )}

      {hasData && !isFormOpen && (
        <Button variant="secondary" size="sm" onClick={() => setIsFormOpen(o => !o)} className="gap-1.5">
          <Plus className="h-4 w-4" />
          {t('New Request')}
        </Button>
      )}

      <CreationForm isOpen={isFormOpen} onClose={() => setIsFormOpen(false)} onCreated={handleCreated} t={t} />
    </div>
  );
};
