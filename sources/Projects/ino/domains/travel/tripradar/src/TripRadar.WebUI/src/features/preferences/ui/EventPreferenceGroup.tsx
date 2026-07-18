import type { EventPreferences } from 'shared/api';
import { usePreferenceOptions } from './usePreferenceOptions';
import { PreferenceGroup, SelectField } from './index';
export interface EventPreferenceGroupProps {
  preferences: EventPreferences;
  onChange: (field: keyof EventPreferences, value: EventPreferences[keyof EventPreferences]) => void;
  disabled?: boolean;
}

export const EventPreferenceGroup = ({ preferences, onChange, disabled = false }: EventPreferenceGroupProps) => {
  const { currencyOptions, languageOptions } = usePreferenceOptions();

  return (
    <PreferenceGroup title="Event Preferences" description="Configure your event search and booking preferences">
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
