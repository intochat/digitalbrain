import { Input, SearchInput } from 'shared/ui';
import type { SearchSuggestion } from 'shared/ui';
import type { ScheduledRequestFormState } from '../constants';

interface LocalPlacesFieldsProps {
  formState: ScheduledRequestFormState;
  onChange: (field: keyof ScheduledRequestFormState, value: string) => void;
  t: (key: string) => string;
  locationInputValue: string;
  locationSuggestions: SearchSuggestion[];
  isLocationFetching: boolean;
  onLocationChange: (value: string) => void;
  onLocationSelect: (suggestion: SearchSuggestion) => void;
}

export const LocalPlacesFields = ({
  formState,
  onChange,
  t,
  locationInputValue,
  locationSuggestions,
  isLocationFetching,
  onLocationChange,
  onLocationSelect,
}: LocalPlacesFieldsProps) => (
  <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
    <label className="flex flex-col gap-1.5 sm:col-span-2">
      <span className="text-sm font-medium text-content dark:text-content-dark">{t('Search Query')}</span>
      <Input
        type="text"
        value={formState.searchQuery}
        onChange={e => onChange('searchQuery', e.target.value)}
        placeholder={t('cafes, EV charging, coworking spaces...')}
      />
    </label>
    <div className="flex flex-col gap-1.5">
      <SearchInput
        value={locationInputValue}
        onChange={onLocationChange}
        onSelect={onLocationSelect}
        suggestions={locationSuggestions}
        isFetching={isLocationFetching}
        placeholder={t('City or neighborhood')}
        label={<span className="text-sm font-medium text-content dark:text-content-dark">{t('Location')}</span>}
        searchingLabel={t('Searching...')}
        noResultsLabel={t('No locations found')}
        aria-label={t('Location')}
      />
    </div>
    <label className="flex flex-col gap-1.5">
      <span className="text-sm font-medium text-content dark:text-content-dark">{t('Radius (meters, optional)')}</span>
      <Input
        type="number"
        min="1"
        value={formState.radius}
        onChange={e => onChange('radius', e.target.value)}
        placeholder="2500"
      />
    </label>
  </div>
);
