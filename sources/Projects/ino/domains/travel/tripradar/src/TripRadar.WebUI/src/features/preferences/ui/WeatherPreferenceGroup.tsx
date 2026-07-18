import type { WeatherPreferences, WeatherUnitsType } from 'shared/api';
import { PreferenceGroup, FieldSection } from './index';
import { SelectField, NumberField, ToggleField } from './index';

export interface WeatherPreferenceGroupProps {
  preferences: WeatherPreferences;
  onChange: (field: keyof WeatherPreferences, value: WeatherPreferences[keyof WeatherPreferences]) => void;
  disabled?: boolean;
}

const WEATHER_UNITS_OPTIONS = [
  { value: 'metric', label: 'Metric (°C, km/h, mm)' },
  { value: 'imperial', label: 'Imperial (°F, mph, in)' },
  { value: 'kelvin', label: 'Kelvin (K, m/s, mm)' },
];

export const WeatherPreferenceGroup = ({ preferences, onChange, disabled = false }: WeatherPreferenceGroupProps) => {
  return (
    <PreferenceGroup
      title="Weather Preferences"
      icon="🌤️"
      description="Configure your default weather information preferences"
    >
      <FieldSection variant="compact">
        <SelectField
          label="Units"
          description="Temperature and measurement units"
          value={preferences.Units ?? 'metric'}
          options={WEATHER_UNITS_OPTIONS}
          onChange={value => onChange('Units', value as WeatherUnitsType)}
          disabled={disabled}
        />

        <ToggleField
          label="Include Forecast"
          description="Include weather forecast in responses"
          value={preferences.IncludeForecast ?? true}
          onChange={value => onChange('IncludeForecast', value)}
          disabled={disabled}
        />

        <NumberField
          label="Forecast Days"
          description="Number of forecast days to include"
          value={preferences.ForecastDays ?? 5}
          onChange={value => onChange('ForecastDays', value)}
          min={1}
          max={14}
          disabled={disabled || !preferences.IncludeForecast}
        />
      </FieldSection>
    </PreferenceGroup>
  );
};
