import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { LocationSuggestionItem } from 'entities/search';
import { useLocationSuggestionsQuery } from 'entities/search';
import type { SearchSuggestion } from 'shared/ui';

interface UseLocationSearchOptions {
  initialValue?: string;
  onSelect?: (locationName: string) => void;
}

const getFullLocationLabel = (location: LocationSuggestionItem): string => {
  const canonical = location.canonicalName?.trim();
  const name = location.name?.trim();

  if (canonical) return canonical;
  if (name) return name;
  return String(location.locationId);
};

const toSearchSuggestion = (location: LocationSuggestionItem): SearchSuggestion => {
  const label = getFullLocationLabel(location);
  const fallbackName = location.name?.trim();
  const secondary = fallbackName && fallbackName !== label ? fallbackName : undefined;

  return {
    key: String(location.locationId),
    label,
    secondary,
    badge: location.countryCode,
  };
};

export const useLocationSearch = ({ initialValue = '', onSelect }: UseLocationSearchOptions = {}) => {
  const [inputValue, setInputValue] = useState(initialValue);
  const [debouncedQuery, setDebouncedQuery] = useState('');
  const knownLocationsRef = useRef<Map<string, LocationSuggestionItem>>(new Map());

  useEffect(() => {
    const id = window.setTimeout(() => setDebouncedQuery(inputValue), 250);
    return () => window.clearTimeout(id);
  }, [inputValue]);

  const { data: locations = [], isFetching } = useLocationSuggestionsQuery(debouncedQuery, 8);

  useEffect(() => {
    for (const location of locations) {
      knownLocationsRef.current.set(String(location.locationId), location);
    }
  }, [locations]);

  const suggestions: SearchSuggestion[] = useMemo(() => locations.map(toSearchSuggestion), [locations]);

  const handleChange = useCallback((value: string) => {
    setInputValue(value);
  }, []);

  const handleSelect = useCallback(
    (suggestion: SearchSuggestion) => {
      const selected = knownLocationsRef.current.get(suggestion.key);
      const selectedName = selected ? getFullLocationLabel(selected) : suggestion.label.trim();
      setInputValue(selectedName);
      onSelect?.(selectedName);
    },
    [onSelect]
  );

  const reset = useCallback(() => {
    setInputValue('');
    setDebouncedQuery('');
  }, []);

  return {
    inputValue,
    suggestions,
    isFetching,
    handleChange,
    handleSelect,
    reset,
    setInputValue,
  };
};
