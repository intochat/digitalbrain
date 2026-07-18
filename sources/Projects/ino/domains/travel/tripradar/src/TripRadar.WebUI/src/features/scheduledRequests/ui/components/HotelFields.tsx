import { DatePicker, SearchInput } from 'shared/ui';
import type { SearchSuggestion } from 'shared/ui';
import type { ScheduledRequestFormState } from '../constants';

interface HotelFieldsProps {
  formState: ScheduledRequestFormState;
  onChange: (field: keyof ScheduledRequestFormState, value: string) => void;
  t: (key: string) => string;
  locationInputValue: string;
  locationSuggestions: SearchSuggestion[];
  isLocationFetching: boolean;
  onLocationChange: (value: string) => void;
  onLocationSelect: (suggestion: SearchSuggestion) => void;
}

export const HotelFields = ({
  formState,
  onChange,
  t,
  locationInputValue,
  locationSuggestions,
  isLocationFetching,
  onLocationChange,
  onLocationSelect,
}: HotelFieldsProps) => (
  <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
    <div className="flex flex-col gap-1.5 sm:col-span-2">
      <SearchInput
        value={locationInputValue}
        onChange={onLocationChange}
        onSelect={onLocationSelect}
        suggestions={locationSuggestions}
        isFetching={isLocationFetching}
        placeholder={t('Barcelona, Paris, Bangkok...')}
        label={<span className="text-sm font-medium text-content dark:text-content-dark">{t('Location')}</span>}
        searchingLabel={t('Searching...')}
        noResultsLabel={t('No locations found')}
        aria-label={t('Location')}
      />
    </div>
    <div className="flex flex-col gap-1.5">
      <span className="text-sm font-medium text-content dark:text-content-dark">{t('Check-in Date')}</span>
      <DatePicker
        min={new Date().toISOString().split('T')[0]}
        value={formState.checkInDate}
        onChange={v => onChange('checkInDate', v)}
        placeholder={t('Select date')}
        aria-label={t('Check-in Date')}
      />
    </div>
    <div className="flex flex-col gap-1.5">
      <span className="text-sm font-medium text-content dark:text-content-dark">{t('Check-out Date')}</span>
      <DatePicker
        min={formState.checkInDate || new Date().toISOString().split('T')[0]}
        value={formState.checkOutDate}
        onChange={v => onChange('checkOutDate', v)}
        placeholder={t('Select date')}
        aria-label={t('Check-out Date')}
      />
    </div>
  </div>
);
