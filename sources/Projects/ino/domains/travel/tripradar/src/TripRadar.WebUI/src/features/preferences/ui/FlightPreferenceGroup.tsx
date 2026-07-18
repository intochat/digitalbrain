import type { FlightPreferences, TravelClassType } from 'shared/api';
import { usePreferenceOptions } from './usePreferenceOptions';
import { FieldSection, NumberField, PreferenceGroup, SelectField } from './index';
export interface FlightPreferenceGroupProps {
  preferences: FlightPreferences;
  onChange: (field: keyof FlightPreferences, value: FlightPreferences[keyof FlightPreferences]) => void;
  disabled?: boolean;
}

const TRAVEL_CLASS_OPTIONS = [
  { value: 'economy', label: 'Economy' },
  { value: 'premiumEconomy', label: 'Premium Economy' },
  { value: 'business', label: 'Business' },
  { value: 'first', label: 'First Class' },
];

const FLIGHT_SORT_OPTIONS = [
  { value: 'topFlights', label: 'Top Flights' },
  { value: 'price', label: 'Price' },
  { value: 'departureTime', label: 'Departure Time' },
  { value: 'arrivalTime', label: 'Arrival Time' },
  { value: 'duration', label: 'Duration' },
  { value: 'emissions', label: 'Emissions' },
];

export const FlightPreferenceGroup = ({ preferences, onChange, disabled = false }: FlightPreferenceGroupProps) => {
  const { currencyOptions } = usePreferenceOptions();

  return (
    <PreferenceGroup title="Flight Preferences" description="Configure your default flight search preferences">
      <FieldSection title="Passengers">
        <NumberField
          label="Adults"
          value={preferences.Adults ?? 1}
          onChange={value => onChange('Adults', value)}
          min={1}
          max={9}
          disabled={disabled}
          required
        />
        <NumberField
          label="Children (2-11 years)"
          value={preferences.Children ?? 0}
          onChange={value => onChange('Children', value)}
          min={0}
          max={8}
          disabled={disabled}
        />
        <NumberField
          label="Infants in Seat"
          value={preferences.InfantsInSeat ?? 0}
          onChange={value => onChange('InfantsInSeat', value)}
          min={0}
          max={4}
          disabled={disabled}
        />
        <NumberField
          label="Infants on Lap"
          value={preferences.InfantsOnLap ?? 0}
          onChange={value => onChange('InfantsOnLap', value)}
          min={0}
          max={4}
          disabled={disabled}
        />
      </FieldSection>

      <FieldSection title="Travel Options">
        <SelectField
          label="Travel Class"
          value={preferences.TravelClass ?? 'economy'}
          options={TRAVEL_CLASS_OPTIONS}
          onChange={value => onChange('TravelClass', value as TravelClassType)}
          disabled={disabled}
        />
        <SelectField
          label="Sort By"
          value={preferences.SortBy ?? 'topFlights'}
          options={FLIGHT_SORT_OPTIONS}
          onChange={value => onChange('SortBy', value as string)}
          disabled={disabled}
        />
      </FieldSection>

      <FieldSection title="Pricing">
        <NumberField
          label="Max Price (per person)"
          value={preferences.MaxPrice ?? 1000}
          onChange={value => onChange('MaxPrice', value)}
          min={0}
          max={50000}
          step={50}
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
    </PreferenceGroup>
  );
};
