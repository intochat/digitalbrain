import type { MapsPreferences } from 'shared/api';
import { usePreferenceOptions } from './usePreferenceOptions';
import { PreferenceGroup, SelectField } from './index';
export interface MapsPreferenceGroupProps {
  preferences: MapsPreferences;
  onChange: (field: keyof MapsPreferences, value: MapsPreferences[keyof MapsPreferences]) => void;
  disabled?: boolean;
}

export const MapsPreferenceGroup = ({ preferences, onChange, disabled = false }: MapsPreferenceGroupProps) => {
  const { currencyOptions, languageOptions } = usePreferenceOptions();

  return (
    <PreferenceGroup title="Maps Preferences" description="Configure your maps and navigation preferences">
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
