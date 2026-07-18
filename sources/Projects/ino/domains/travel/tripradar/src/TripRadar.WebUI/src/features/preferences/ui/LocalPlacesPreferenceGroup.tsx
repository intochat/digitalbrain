import type { LocalPlacesPreferences } from 'shared/api';
import { usePreferenceOptions } from './usePreferenceOptions';
import { PreferenceGroup, SelectField } from './index';
export interface LocalPlacesPreferenceGroupProps {
  preferences: LocalPlacesPreferences;
  onChange: (field: keyof LocalPlacesPreferences, value: LocalPlacesPreferences[keyof LocalPlacesPreferences]) => void;
  disabled?: boolean;
}

export const LocalPlacesPreferenceGroup = ({
  preferences,
  onChange,
  disabled = false,
}: LocalPlacesPreferenceGroupProps) => {
  const { currencyOptions, languageOptions } = usePreferenceOptions();

  return (
    <PreferenceGroup title="Local Places Preferences" description="Configure your local places search preferences">
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
        <SelectField
          label="Currency"
          value={preferences.Currency ?? 'USD'}
          options={currencyOptions}
          onChange={value => onChange('Currency', value as string)}
          disabled={disabled}
        />
        <SelectField
          label="Language"
          value={preferences.Language ?? 'en'}
          options={languageOptions}
          onChange={value => onChange('Language', value as string)}
          disabled={disabled}
        />
      </div>
    </PreferenceGroup>
  );
};
