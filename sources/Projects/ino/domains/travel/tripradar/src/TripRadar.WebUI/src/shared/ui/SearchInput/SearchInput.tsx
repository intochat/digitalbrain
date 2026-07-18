import type { ReactNode } from 'react';
import { useCallback, useEffect, useRef, useState } from 'react';
import { Input } from '../Input';

export interface SearchSuggestion {
  key: string;
  label: string;
  secondary?: string;
  badge?: string;
}

interface SearchInputProps {
  value: string;
  onChange: (value: string) => void;
  onSelect: (suggestion: SearchSuggestion) => void;
  suggestions: SearchSuggestion[];
  isFetching: boolean;
  placeholder?: string;
  label?: ReactNode;
  searchingLabel: string;
  noResultsLabel: string;
  minQueryLength?: number;
  'aria-label'?: string;
}

export const SearchInput = ({
  value,
  onChange,
  onSelect,
  suggestions,
  isFetching,
  placeholder,
  label,
  searchingLabel,
  noResultsLabel,
  minQueryLength = 2,
  'aria-label': ariaLabel,
}: SearchInputProps) => {
  const [isOpen, setIsOpen] = useState(false);
  const blurTimeoutRef = useRef<ReturnType<typeof setTimeout>>();

  const showDropdown = isOpen && value.trim().length >= minQueryLength;

  const handleFocus = useCallback(() => {
    setIsOpen(true);
  }, []);

  const handleBlur = useCallback(() => {
    blurTimeoutRef.current = setTimeout(() => {
      setIsOpen(false);
    }, 150);
  }, []);

  const handleSelect = useCallback(
    (suggestion: SearchSuggestion) => {
      if (blurTimeoutRef.current) {
        clearTimeout(blurTimeoutRef.current);
      }
      onSelect(suggestion);
      setIsOpen(false);
    },
    [onSelect]
  );

  useEffect(() => {
    return () => {
      if (blurTimeoutRef.current) {
        clearTimeout(blurTimeoutRef.current);
      }
    };
  }, []);

  const handleKeyDown = useCallback((e: React.KeyboardEvent) => {
    if (e.key === 'Escape') {
      setIsOpen(false);
    }
  }, []);

  return (
    <div className="relative">
      {label ? <div className="mb-1.5">{label}</div> : null}
      <Input
        type="text"
        value={value}
        onChange={e => onChange(e.target.value)}
        onFocus={handleFocus}
        onBlur={handleBlur}
        onKeyDown={handleKeyDown}
        placeholder={placeholder}
        aria-label={ariaLabel}
        autoComplete="off"
      />
      {showDropdown ? (
        <div className="absolute top-full z-20 mt-1 max-h-56 w-full overflow-auto rounded-lg border border-outline dark:border-outline-dark bg-surface dark:bg-surface-dark shadow-lg">
          {isFetching ? (
            <p className="px-3 py-2 text-xs text-content-secondary dark:text-content-secondary-dark">
              {searchingLabel}
            </p>
          ) : null}
          {!isFetching && suggestions.length === 0 ? (
            <p className="px-3 py-2 text-xs text-content-secondary dark:text-content-secondary-dark">
              {noResultsLabel}
            </p>
          ) : null}
          {!isFetching && suggestions.length > 0 ? (
            <ul className="py-1" role="listbox">
              {suggestions.map(suggestion => (
                <li key={suggestion.key} role="option" aria-selected={false}>
                  <button
                    type="button"
                    className="w-full px-3 py-2 text-left hover:bg-surface-accent dark:hover:bg-surface-accent-dark transition-colors"
                    onMouseDown={e => {
                      e.preventDefault();
                      handleSelect(suggestion);
                    }}
                  >
                    <p className="text-sm text-content dark:text-content-dark">{suggestion.label}</p>
                    {suggestion.secondary ? (
                      <p className="text-xs text-content-secondary dark:text-content-secondary-dark">
                        {suggestion.secondary}
                      </p>
                    ) : null}
                    {suggestion.badge ? (
                      <p className="text-xs font-medium text-content-muted dark:text-content-muted-dark mt-0.5">
                        {suggestion.badge}
                      </p>
                    ) : null}
                  </button>
                </li>
              ))}
            </ul>
          ) : null}
        </div>
      ) : null}
    </div>
  );
};
