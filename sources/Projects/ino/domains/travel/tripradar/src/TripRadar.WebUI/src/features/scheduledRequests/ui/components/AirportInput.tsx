import { frontendI18n } from 'app/i18n';
import type { AirportSuggestionItem } from 'entities/scheduledRequests';
import { Input } from 'shared/ui';
import { getAirportSuggestionDisplay } from '../utils';

interface AirportInputProps {
  label: string;
  value: string;
  suggestions: AirportSuggestionItem[];
  isFetching: boolean;
  showSuggestions: boolean;
  onChange: (value: string) => void;
  onFocus: () => void;
  onBlur: () => void;
  onSelect: (airport: AirportSuggestionItem) => void;
  placeholder: string;
  searchingLabel: string;
  noResultsLabel: string;
}

export const AirportInput = ({
  label,
  value,
  suggestions,
  isFetching,
  showSuggestions,
  onChange,
  onFocus,
  onBlur,
  onSelect,
  placeholder,
  searchingLabel,
  noResultsLabel,
}: AirportInputProps) => (
  <label className="flex flex-col gap-1.5 relative">
    <span className="text-sm font-medium text-content dark:text-content-dark">{label}</span>
    <Input
      type="text"
      value={value}
      onChange={e => onChange(e.target.value)}
      onFocus={onFocus}
      onBlur={onBlur}
      placeholder={placeholder}
      aria-label={label}
      autoComplete="off"
    />
    {showSuggestions && (
      <div className="absolute top-full z-20 mt-1 max-h-56 w-full overflow-auto rounded-lg border border-outline dark:border-outline-dark bg-surface dark:bg-surface-dark shadow-lg">
        {isFetching && (
          <p className="px-3 py-2 text-xs text-content-secondary dark:text-content-secondary-dark">{searchingLabel}</p>
        )}
        {!isFetching && suggestions.length === 0 && (
          <p className="px-3 py-2 text-xs text-content-secondary dark:text-content-secondary-dark">{noResultsLabel}</p>
        )}
        {!isFetching && suggestions.length > 0 && (
          <ul className="py-1">
            {suggestions.map(airport => {
              const display = getAirportSuggestionDisplay(
                airport,
                value,
                frontendI18n.resolvedLanguage ?? frontendI18n.language
              );

              return (
                <li key={`${airport.code}-${airport.city}-${airport.name}`}>
                  <button
                    type="button"
                    className="w-full px-3 py-2 text-left hover:bg-surface-accent dark:hover:bg-surface-accent-dark transition-colors"
                    onMouseDown={e => {
                      e.preventDefault();
                      onSelect(airport);
                    }}
                  >
                    <p className="text-sm text-content dark:text-content-dark">{display.locationLabel}</p>
                    <p className="text-xs text-content-secondary dark:text-content-secondary-dark">
                      {display.airportLabel}
                    </p>
                    <p className="text-xs font-medium text-primary-600 dark:text-primary-300 mt-0.5">{airport.code}</p>
                  </button>
                </li>
              );
            })}
          </ul>
        )}
      </div>
    )}
  </label>
);
