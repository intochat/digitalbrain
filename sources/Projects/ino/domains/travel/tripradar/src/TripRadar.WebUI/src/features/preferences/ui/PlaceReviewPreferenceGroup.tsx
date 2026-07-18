import type { PlaceReviewPreferences } from 'shared/api';
import { PreferenceGroup } from './PreferenceGroup';
import { usePreferenceOptions } from './usePreferenceOptions';
import { SelectField } from './index';

export interface PlaceReviewPreferenceGroupProps {
  preferences: PlaceReviewPreferences;
  onChange: (field: keyof PlaceReviewPreferences, value: PlaceReviewPreferences[keyof PlaceReviewPreferences]) => void;
  disabled?: boolean;
}

const SORT_BY_OPTIONS = [
  { value: 'newest', label: 'Newest First' },
  { value: 'oldest', label: 'Oldest First' },
  { value: 'rating_high', label: 'Highest Rating' },
  { value: 'rating_low', label: 'Lowest Rating' },
  { value: 'helpful', label: 'Most Helpful' },
];

export const PlaceReviewPreferenceGroup = ({
  preferences,
  onChange,
  disabled = false,
}: PlaceReviewPreferenceGroupProps) => {
  const { currencyOptions, languageOptions } = usePreferenceOptions();
  return (
    <PreferenceGroup
      title="Place Review Preferences"
      icon="⭐"
      description="Configure your place review and rating preferences"
    >
      <SelectField
        label="Currency"
        description="Preferred currency for price-related reviews"
        value={preferences.Currency ?? 'USD'}
        options={currencyOptions}
        onChange={value => onChange('Currency', value as string)}
        disabled={disabled}
      />

      <SelectField
        label="Language"
        description="Preferred language for reviews"
        value={preferences.Language ?? 'en'}
        options={languageOptions}
        onChange={value => onChange('Language', value as string)}
        disabled={disabled}
      />

      <SelectField
        label="Sort By"
        description="Default sorting for reviews"
        value={preferences.SortBy ?? 'newest'}
        options={SORT_BY_OPTIONS}
        onChange={value => onChange('SortBy', value as string)}
        disabled={disabled}
      />
    </PreferenceGroup>
  );
};
