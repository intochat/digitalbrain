import type { HolidayPreferences } from 'shared/api';
import { PreferenceGroup } from './PreferenceGroup';
import { SelectField } from './index';

export interface HolidayPreferenceGroupProps {
  preferences: HolidayPreferences;
  onChange: (field: keyof HolidayPreferences, value: HolidayPreferences[keyof HolidayPreferences]) => void;
  disabled?: boolean;
}

const LANGUAGE_OPTIONS = [
  { value: 'en', label: 'English' },
  { value: 'es', label: 'Spanish' },
  { value: 'fr', label: 'French' },
  { value: 'de', label: 'German' },
  { value: 'it', label: 'Italian' },
  { value: 'pt', label: 'Portuguese' },
  { value: 'ru', label: 'Russian' },
  { value: 'ja', label: 'Japanese' },
  { value: 'ko', label: 'Korean' },
  { value: 'zh', label: 'Chinese' },
];

const HOLIDAY_TYPE_OPTIONS = [
  { value: 'public', label: 'Public Holidays' },
  { value: 'religious', label: 'Religious Holidays' },
  { value: 'cultural', label: 'Cultural Holidays' },
  { value: 'national', label: 'National Holidays' },
  { value: 'international', label: 'International Holidays' },
];

export const HolidayPreferenceGroup = ({ preferences, onChange, disabled = false }: HolidayPreferenceGroupProps) => {
  return (
    <PreferenceGroup title="Holiday Preferences" icon="🎊" description="Configure your holiday information preferences">
      <SelectField
        label="Language"
        description="Preferred language for holiday information"
        value={preferences.Language ?? 'en'}
        options={LANGUAGE_OPTIONS}
        onChange={value => onChange('Language', value as string)}
        disabled={disabled}
      />

      <SelectField
        label="Holiday Type"
        description="Type of holidays to include"
        value={preferences.Type ?? 'public'}
        options={HOLIDAY_TYPE_OPTIONS}
        onChange={value => onChange('Type', value as string)}
        disabled={disabled}
      />
    </PreferenceGroup>
  );
};
