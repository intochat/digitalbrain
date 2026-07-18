import { DatePicker, Input, SearchInput } from 'shared/ui';
import type { SearchSuggestion } from 'shared/ui';
import type { ScheduledRequestFormState } from '../constants';

interface EventFieldsProps {
  formState: ScheduledRequestFormState;
  onChange: (field: keyof ScheduledRequestFormState, value: string) => void;
  t: (key: string) => string;
  locationInputValue: string;
  locationSuggestions: SearchSuggestion[];
  isLocationFetching: boolean;
  onLocationChange: (value: string) => void;
  onLocationSelect: (suggestion: SearchSuggestion) => void;
}

export const EventFields = ({
  formState,
  onChange,
  t,
  locationInputValue,
  locationSuggestions,
  isLocationFetching,
  onLocationChange,
  onLocationSelect,
}: EventFieldsProps) => (
  <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
    <label className="flex flex-col gap-1.5 sm:col-span-2">
      <span className="text-sm font-medium text-content dark:text-content-dark">{t('Search Query')}</span>
      <Input
        type="text"
        value={formState.searchQuery}
        onChange={e => onChange('searchQuery', e.target.value)}
        placeholder={t('concerts, festivals, museums...')}
      />
    </label>
    <div className="flex flex-col gap-1.5 sm:col-span-2">
      <SearchInput
        value={locationInputValue}
        onChange={onLocationChange}
        onSelect={onLocationSelect}
        suggestions={locationSuggestions}
        isFetching={isLocationFetching}
        placeholder={t('New York, London, Tokyo...')}
        label={<span className="text-sm font-medium text-content dark:text-content-dark">{t('Location')}</span>}
        searchingLabel={t('Searching...')}
        noResultsLabel={t('No locations found')}
        aria-label={t('Location')}
      />
    </div>
    <div className="flex flex-col gap-1.5">
      <span className="text-sm font-medium text-content dark:text-content-dark">{t('Start Date (Optional)')}</span>
      <DatePicker
        min={new Date().toISOString().split('T')[0]}
        value={formState.startDate}
        onChange={v => onChange('startDate', v)}
        placeholder={t('Select date')}
        aria-label={t('Start Date')}
      />
    </div>
    <div className="flex flex-col gap-1.5">
      <span className="text-sm font-medium text-content dark:text-content-dark">{t('End Date (Optional)')}</span>
      <DatePicker
        min={formState.startDate || new Date().toISOString().split('T')[0]}
        value={formState.endDate}
        onChange={v => onChange('endDate', v)}
        placeholder={t('Select date')}
        aria-label={t('End Date')}
      />
    </div>
  </div>
);
