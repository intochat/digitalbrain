import type { AirportSuggestionItem } from 'entities/scheduledRequests';
import { DatePicker } from 'shared/ui';
import type { ScheduledRequestFormState } from '../constants';
import { AirportInput } from './AirportInput';

interface FlightFieldsProps {
  formState: ScheduledRequestFormState;
  onChange: (field: keyof ScheduledRequestFormState, value: string) => void;
  departureAirportInput: string;
  destinationAirportInput: string;
  departureSuggestions: AirportSuggestionItem[];
  destinationSuggestions: AirportSuggestionItem[];
  isDepartureFetching: boolean;
  isDestinationFetching: boolean;
  showDepartureSuggestions: boolean;
  showDestinationSuggestions: boolean;
  onDepartureChange: (value: string) => void;
  onDestinationChange: (value: string) => void;
  onDepartureFocus: () => void;
  onDestinationFocus: () => void;
  onDepartureBlur: () => void;
  onDestinationBlur: () => void;
  onDepartureSelect: (airport: AirportSuggestionItem) => void;
  onDestinationSelect: (airport: AirportSuggestionItem) => void;
  t: (key: string, params?: Record<string, string>) => string;
}

export const FlightFields = ({
  formState,
  onChange,
  departureAirportInput,
  destinationAirportInput,
  departureSuggestions,
  destinationSuggestions,
  isDepartureFetching,
  isDestinationFetching,
  showDepartureSuggestions,
  showDestinationSuggestions,
  onDepartureChange,
  onDestinationChange,
  onDepartureFocus,
  onDestinationFocus,
  onDepartureBlur,
  onDestinationBlur,
  onDepartureSelect,
  onDestinationSelect,
  t,
}: FlightFieldsProps) => (
  <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
    <AirportInput
      label={t('Origin City or Airport')}
      value={departureAirportInput}
      suggestions={departureSuggestions}
      isFetching={isDepartureFetching}
      showSuggestions={showDepartureSuggestions}
      onChange={onDepartureChange}
      onFocus={onDepartureFocus}
      onBlur={onDepartureBlur}
      onSelect={onDepartureSelect}
      placeholder={t('Search city or airport (e.g., New York, JFK)')}
      searchingLabel={t('Searching airports...')}
      noResultsLabel={t('No airports found.')}
    />
    <AirportInput
      label={t('Destination City or Airport')}
      value={destinationAirportInput}
      suggestions={destinationSuggestions}
      isFetching={isDestinationFetching}
      showSuggestions={showDestinationSuggestions}
      onChange={onDestinationChange}
      onFocus={onDestinationFocus}
      onBlur={onDestinationBlur}
      onSelect={onDestinationSelect}
      placeholder={t('Search city or airport (e.g., London, LHR)')}
      searchingLabel={t('Searching airports...')}
      noResultsLabel={t('No airports found.')}
    />
    <div className="flex flex-col gap-1.5">
      <span className="text-sm font-medium text-content dark:text-content-dark">{t('Departure Date')}</span>
      <DatePicker
        min={new Date().toISOString().split('T')[0]}
        value={formState.departureDate}
        onChange={v => onChange('departureDate', v)}
        placeholder={t('Select date')}
        aria-label={t('Departure Date')}
      />
    </div>
    <div className="flex flex-col gap-1.5">
      <span className="text-sm font-medium text-content dark:text-content-dark">{t('Return Date (Optional)')}</span>
      <DatePicker
        min={formState.departureDate || new Date().toISOString().split('T')[0]}
        value={formState.returnDate}
        onChange={v => onChange('returnDate', v)}
        placeholder={t('Select date')}
        aria-label={t('Return Date')}
      />
    </div>
  </div>
);
