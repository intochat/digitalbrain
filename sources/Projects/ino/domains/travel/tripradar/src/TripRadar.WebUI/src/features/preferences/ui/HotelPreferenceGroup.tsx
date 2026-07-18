import type { HotelPreferences, HotelSortByType, HotelRatingFilterType } from 'shared/api';
import { usePreferenceOptions } from './usePreferenceOptions';
import { FieldSection, NumberField, PreferenceGroup, SelectField, ToggleField } from './index';
export interface HotelPreferenceGroupProps {
  preferences: HotelPreferences;
  onChange: (field: keyof HotelPreferences, value: HotelPreferences[keyof HotelPreferences]) => void;
  disabled?: boolean;
}

const HOTEL_SORT_OPTIONS = [
  { value: 'lowestPrice', label: 'Lowest Price' },
  { value: 'highestRating', label: 'Highest Rating' },
  { value: 'mostReviewed', label: 'Most Reviewed' },
];

const HOTEL_RATING_OPTIONS = [
  { value: 'rating35Plus', label: '3.5+ Stars' },
  { value: 'rating40Plus', label: '4.0+ Stars' },
  { value: 'rating45Plus', label: '4.5+ Stars' },
];

export const HotelPreferenceGroup = ({ preferences, onChange, disabled = false }: HotelPreferenceGroupProps) => {
  const { currencyOptions } = usePreferenceOptions();

  return (
    <PreferenceGroup title="Hotel Preferences" description="Configure your default hotel search preferences">
      <FieldSection title="Guests">
        <NumberField
          label="Adults"
          value={preferences.Adults ?? 2}
          onChange={value => onChange('Adults', value)}
          min={1}
          max={10}
          disabled={disabled}
          required
        />
        <NumberField
          label="Children"
          value={preferences.Children ?? 0}
          onChange={value => onChange('Children', value)}
          min={0}
          max={8}
          disabled={disabled}
        />
      </FieldSection>

      <FieldSection title="Budget">
        <NumberField
          label="Min Price (per night)"
          value={preferences.MinPrice ?? 0}
          onChange={value => onChange('MinPrice', value)}
          min={0}
          max={10000}
          step={10}
          disabled={disabled}
        />
        <NumberField
          label="Max Price (per night)"
          value={preferences.MaxPrice ?? 500}
          onChange={value => onChange('MaxPrice', value)}
          min={0}
          max={10000}
          step={10}
          disabled={disabled}
        />
        <SelectField
          label="Currency"
          value={preferences.Currency ?? 'USD'}
          options={currencyOptions}
          onChange={value => onChange('Currency', value as string)}
          disabled={disabled}
        />
      </FieldSection>

      <FieldSection title="Search">
        <SelectField
          label="Sort By"
          value={preferences.SortBy ?? 'lowestPrice'}
          options={HOTEL_SORT_OPTIONS}
          onChange={value => onChange('SortBy', value as HotelSortByType)}
          disabled={disabled}
        />
        <SelectField
          label="Rating Filter"
          value={preferences.Rating ?? 'rating35Plus'}
          options={HOTEL_RATING_OPTIONS}
          onChange={value => onChange('Rating', value as HotelRatingFilterType)}
          disabled={disabled}
        />
        <ToggleField
          label="Free Cancellation"
          description="Only show hotels with free cancellation"
          value={preferences.FreeCancellation ?? false}
          onChange={value => onChange('FreeCancellation', value)}
          disabled={disabled}
        />
      </FieldSection>
    </PreferenceGroup>
  );
};
