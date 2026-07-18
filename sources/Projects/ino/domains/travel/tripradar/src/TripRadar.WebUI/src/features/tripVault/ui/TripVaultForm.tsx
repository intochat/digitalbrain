import type { FormEvent } from 'react';
import { useFrontendLanguage } from 'app/providers';
import { Button, DatePicker, Input, Textarea } from 'shared/ui';
import type { TripVaultFormState } from './tripVaultUtils';

interface TripVaultFormProps {
  formState: TripVaultFormState;
  editingTripUniqueId: string | null;
  isAnyMutationPending: boolean;
  todayDateInputValue: string;
  onInputChange: (field: keyof TripVaultFormState, value: string) => void;
  onSubmit: (event: FormEvent<HTMLFormElement>) => void;
  onCancel: () => void;
}

export const TripVaultForm = ({
  formState,
  editingTripUniqueId,
  isAnyMutationPending,
  todayDateInputValue,
  onInputChange,
  onSubmit,
  onCancel,
}: TripVaultFormProps) => {
  const { t } = useFrontendLanguage();
  const isEditing = Boolean(editingTripUniqueId);

  return (
    <form onSubmit={onSubmit} className="space-y-4">
      <label className="flex flex-col gap-1.5">
        <span className="text-sm font-medium text-content dark:text-content-dark">{t('Trip Name')}</span>
        <Input
          type="text"
          value={formState.name}
          onChange={event => onInputChange('name', event.target.value)}
          placeholder={t('e.g. Spring in Lisbon')}
          maxLength={255}
          aria-label={t('Trip Name')}
        />
      </label>

      <label className="flex flex-col gap-1.5">
        <span className="text-sm font-medium text-content dark:text-content-dark">{t('AI Context / Description')}</span>
        <Textarea
          value={formState.description}
          onChange={event => onInputChange('description', event.target.value)}
          rows={3}
          maxLength={2000}
          aria-label={t('AI Context / Description')}
          placeholder={t('Goals, budget style, constraints, preferences...')}
        />
      </label>

      <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
        <div className="flex flex-col gap-1.5">
          <span className="text-sm font-medium text-content dark:text-content-dark">{t('Start Date')}</span>
          <DatePicker
            value={formState.startDate}
            onChange={v => onInputChange('startDate', v)}
            min={todayDateInputValue}
            placeholder={t('Select start date')}
            aria-label={t('Start Date')}
          />
        </div>
        <div className="flex flex-col gap-1.5">
          <span className="text-sm font-medium text-content dark:text-content-dark">{t('End Date')}</span>
          <DatePicker
            value={formState.endDate}
            onChange={v => onInputChange('endDate', v)}
            min={formState.startDate || todayDateInputValue}
            placeholder={t('Select end date')}
            aria-label={t('End Date')}
          />
        </div>
      </div>

      <div className="flex flex-wrap gap-2 pt-1">
        <Button type="submit" disabled={isAnyMutationPending} isLoading={isAnyMutationPending}>
          {isEditing ? t('Save Changes') : t('Create Trip')}
        </Button>
        <Button type="button" variant="secondary" onClick={onCancel}>
          {t('Cancel')}
        </Button>
      </div>
    </form>
  );
};
