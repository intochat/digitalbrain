import { useCallback, useEffect, useMemo, useState } from 'react';
import { useAirportSuggestionsQuery } from 'entities/scheduledRequests';
import type { AirportSuggestionItem } from 'entities/scheduledRequests';
import type { ScheduledQueryType } from 'entities/scheduledRequests';
import { DatePicker, Dropdown, Input } from 'shared/ui';
import type { DropdownOption } from 'shared/ui';
import type { AirportInputKey, ScheduledRequestFormState } from '../constants';
import { QUERY_TYPE_OPTIONS } from '../constants';
import { useLocationSearch } from '../hooks/useLocationSearch';
import {
  combineDateAndTimeInputValue,
  formatAirportSuggestion,
  getDefaultNextExecutionTime,
  isManualIataCodeInput,
  normalizeAirportCode,
  SCHEDULE_OPTIONS,
  toDateInputValue,
  toTimeInputValue,
} from '../utils';
import { EventFields } from './EventFields';
import { FlightFields } from './FlightFields';
import { HotelFields } from './HotelFields';
import { LocalPlacesFields } from './LocalPlacesFields';

interface RequestFormProps {
  formState: ScheduledRequestFormState;
  onChange: (field: keyof ScheduledRequestFormState, value: string) => void;
  disableQueryType?: boolean;
  t: (key: string, params?: Record<string, string | number>) => string;
}

export const RequestForm = ({ formState, onChange, disableQueryType = false, t }: RequestFormProps) => {
  const [departureAirportInput, setDepartureAirportInput] = useState('');
  const [destinationAirportInput, setDestinationAirportInput] = useState('');
  const [debouncedDeparture, setDebouncedDeparture] = useState('');
  const [debouncedDestination, setDebouncedDestination] = useState('');
  const [activeAirportInput, setActiveAirportInput] = useState<AirportInputKey | null>(null);

  const {
    inputValue: locationInputValue,
    suggestions: locationSuggestions,
    isFetching: isLocationFetching,
    handleChange: handleLocationSearchChange,
    handleSelect: handleLocationSearchSelect,
  } = useLocationSearch({
    initialValue: formState.location,
    onSelect: useCallback((locationName: string) => {
      onChange('location', locationName);
    }, [onChange]),
  });

  const nextExecutionDate = toDateInputValue(formState.nextExecutionTime);
  const nextExecutionTime = toTimeInputValue(formState.nextExecutionTime);
  const nextExecutionMinimum = getDefaultNextExecutionTime();
  const nextExecutionMinimumDate = toDateInputValue(nextExecutionMinimum);

  const handleNextExecutionDateChange = (value: string) => {
    onChange('nextExecutionTime', combineDateAndTimeInputValue(value, nextExecutionTime));
  };

  const handleNextExecutionTimeChange = (value: string) => {
    onChange('nextExecutionTime', combineDateAndTimeInputValue(nextExecutionDate, value));
  };

  const isDepartureSearchActive = activeAirportInput === 'departure' && !isManualIataCodeInput(departureAirportInput);
  const isDestinationSearchActive =
    activeAirportInput === 'destination' && !isManualIataCodeInput(destinationAirportInput);

  const departureSuggestionsQuery = useAirportSuggestionsQuery(debouncedDeparture, 8, isDepartureSearchActive);
  const destinationSuggestionsQuery = useAirportSuggestionsQuery(debouncedDestination, 8, isDestinationSearchActive);

  useEffect(() => {
    const id = window.setTimeout(() => setDebouncedDeparture(departureAirportInput), 250);
    return () => window.clearTimeout(id);
  }, [departureAirportInput]);

  useEffect(() => {
    const id = window.setTimeout(() => setDebouncedDestination(destinationAirportInput), 250);
    return () => window.clearTimeout(id);
  }, [destinationAirportInput]);

  const scheduleDropdownOptions: DropdownOption[] = useMemo(() => {
    const base = SCHEDULE_OPTIONS.map(o => ({ value: o.value, label: t(o.label) }));
    if (base.some(o => o.value === formState.schedule)) return base;
    return [...base, { value: formState.schedule, label: t('Custom ({schedule})', { schedule: formState.schedule }) }];
  }, [formState.schedule, t]);

  const queryTypeDropdownOptions: DropdownOption[] = useMemo(
    () => QUERY_TYPE_OPTIONS.map(o => ({ value: o.value, label: t(o.label) })),
    [t]
  );

  const handleAirportInputChange = (
    field: 'departureAirportCode' | 'destinationAirportCode',
    inputKey: AirportInputKey,
    value: string
  ) => {
    const display = isManualIataCodeInput(value) ? normalizeAirportCode(value) : value;
    if (inputKey === 'departure') setDepartureAirportInput(display);
    else setDestinationAirportInput(display);
    setActiveAirportInput(inputKey);
    onChange(field, isManualIataCodeInput(value) ? normalizeAirportCode(value) : '');
  };

  const handleAirportSelect = (
    field: 'departureAirportCode' | 'destinationAirportCode',
    inputKey: AirportInputKey,
    airport: AirportSuggestionItem
  ) => {
    const code = normalizeAirportCode(airport.code);
    onChange(field, code);
    const currentInput = inputKey === 'departure' ? departureAirportInput : destinationAirportInput;
    const formatted = formatAirportSuggestion({ ...airport, code }, currentInput);
    if (inputKey === 'departure') setDepartureAirportInput(formatted);
    else setDestinationAirportInput(formatted);
    setActiveAirportInput(null);
  };

  const shouldShowDeparture =
    activeAirportInput === 'departure' &&
    departureAirportInput.trim().length >= 2 &&
    !isManualIataCodeInput(departureAirportInput);
  const shouldShowDestination =
    activeAirportInput === 'destination' &&
    destinationAirportInput.trim().length >= 2 &&
    !isManualIataCodeInput(destinationAirportInput);

  const handleLocationChange = useCallback(
    (value: string) => {
      handleLocationSearchChange(value);
      onChange('location', value);
    },
    [handleLocationSearchChange, onChange]
  );

  const locationSearchProps = {
    locationInputValue,
    locationSuggestions,
    isLocationFetching,
    onLocationChange: handleLocationChange,
    onLocationSelect: handleLocationSearchSelect,
  };

  const renderQueryTypeFields = () => {
    if (formState.queryType === 'events')
      return <EventFields formState={formState} onChange={onChange} t={t} {...locationSearchProps} />;
    if (formState.queryType === 'hotels')
      return <HotelFields formState={formState} onChange={onChange} t={t} {...locationSearchProps} />;
    if (formState.queryType === 'local-places')
      return <LocalPlacesFields formState={formState} onChange={onChange} t={t} {...locationSearchProps} />;
    return (
      <FlightFields
        formState={formState}
        onChange={onChange}
        departureAirportInput={departureAirportInput}
        destinationAirportInput={destinationAirportInput}
        departureSuggestions={departureSuggestionsQuery.data ?? []}
        destinationSuggestions={destinationSuggestionsQuery.data ?? []}
        isDepartureFetching={departureSuggestionsQuery.isFetching}
        isDestinationFetching={destinationSuggestionsQuery.isFetching}
        showDepartureSuggestions={shouldShowDeparture}
        showDestinationSuggestions={shouldShowDestination}
        onDepartureChange={v => handleAirportInputChange('departureAirportCode', 'departure', v)}
        onDestinationChange={v => handleAirportInputChange('destinationAirportCode', 'destination', v)}
        onDepartureFocus={() => setActiveAirportInput('departure')}
        onDestinationFocus={() => setActiveAirportInput('destination')}
        onDepartureBlur={() => {
          window.setTimeout(() => setActiveAirportInput(c => (c === 'departure' ? null : c)), 120);
        }}
        onDestinationBlur={() => {
          window.setTimeout(() => setActiveAirportInput(c => (c === 'destination' ? null : c)), 120);
        }}
        onDepartureSelect={a => handleAirportSelect('departureAirportCode', 'departure', a)}
        onDestinationSelect={a => handleAirportSelect('destinationAirportCode', 'destination', a)}
        t={t}
      />
    );
  };

  return (
    <div className="space-y-4">
      <div className="flex flex-col gap-1.5">
        <label className="text-sm font-medium text-content dark:text-content-dark">{t('Request Type')}</label>
        <Dropdown
          value={formState.queryType}
          options={queryTypeDropdownOptions}
          onChange={v => onChange('queryType', v as ScheduledQueryType)}
          disabled={disableQueryType}
        />
      </div>
      <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
        <div className="space-y-1.5">
          <label className="text-sm font-medium text-content dark:text-content-dark">{t('Schedule')}</label>
          <Dropdown
            value={formState.schedule}
            options={scheduleDropdownOptions}
            onChange={v => onChange('schedule', v)}
          />
        </div>
        <div className="space-y-1.5">
          <span className="text-sm font-medium text-content dark:text-content-dark">{t('Next Execution Time')}</span>
          <div className="grid grid-cols-1 gap-2 sm:grid-cols-[minmax(0,1fr)_132px]">
            <DatePicker
              min={nextExecutionMinimumDate}
              value={nextExecutionDate}
              onChange={handleNextExecutionDateChange}
              placeholder={t('Select date')}
              aria-label={t('Date')}
            />
            <Input
              type="time"
              step={60}
              value={nextExecutionTime}
              onChange={e => handleNextExecutionTimeChange(e.target.value)}
              aria-label={t('Time')}
            />
          </div>
        </div>
      </div>
      {renderQueryTypeFields()}
    </div>
  );
};

