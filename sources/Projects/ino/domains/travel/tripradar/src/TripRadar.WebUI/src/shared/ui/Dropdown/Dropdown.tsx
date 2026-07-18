import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Check, ChevronDown, Search } from 'lucide-react';
import { createPortal } from 'react-dom';
import { useFrontendLanguage } from 'app/providers';

export interface DropdownOption<T extends string | number = string> {
  value: T;
  label: string;
  icon?: string;
  searchText?: string;
}

interface DropdownProps<T extends string | number = string> {
  value: T;
  options: DropdownOption<T>[];
  onChange: (value: T) => void;
  disabled?: boolean;
  placeholder?: string;
  searchable?: boolean;
  searchPlaceholder?: string;
  noResultsText?: string;
  className?: string;
  'aria-label'?: string;
}

interface TooltipState {
  text: string;
  x: number;
  y: number;
}

const MENU_GAP = 4;
const MENU_MAX_HEIGHT = 264;
const TOOLTIP_DELAY = 150;

const normalizeSearchText = (value: string) =>
  value
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .toLocaleLowerCase();

export const Dropdown = <T extends string | number = string>({
  value,
  options,
  onChange,
  disabled = false,
  placeholder,
  searchable = false,
  searchPlaceholder,
  noResultsText,
  className = '',
  ...rest
}: DropdownProps<T>) => {
  const { t } = useFrontendLanguage();
  const [isOpen, setIsOpen] = useState(false);
  const [search, setSearch] = useState('');
  const [highlightedIndex, setHighlightedIndex] = useState(-1);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const listRef = useRef<HTMLDivElement>(null);
  const searchRef = useRef<HTMLInputElement>(null);
  const [position, setPosition] = useState({ top: 0, left: 0, width: 0, openUp: false });
  const [tooltip, setTooltip] = useState<TooltipState | null>(null);
  const tooltipTimerRef = useRef<ReturnType<typeof setTimeout>>();

  const showTooltip = useCallback((text: string, el: HTMLElement) => {
    clearTimeout(tooltipTimerRef.current);
    tooltipTimerRef.current = setTimeout(() => {
      if (el.scrollWidth <= el.clientWidth) return;
      const rect = el.getBoundingClientRect();
      setTooltip({ text, x: rect.left + rect.width / 2, y: rect.top - 6 });
    }, TOOLTIP_DELAY);
  }, []);

  const hideTooltip = useCallback(() => {
    clearTimeout(tooltipTimerRef.current);
    setTooltip(null);
  }, []);

  const selectedOption = options.find(o => o.value === value);
  const selectedIcon = selectedOption?.icon;
  const resolvedPlaceholder = placeholder ?? t('Select...');
  const resolvedSearchPlaceholder = searchPlaceholder ?? t('Search...');
  const resolvedNoResultsText = noResultsText ?? t('No results');

  const filtered = useMemo(() => {
    if (!search.trim()) return options;

    const normalizedSearch = normalizeSearchText(search);

    return options.filter(option => {
      const searchableText = normalizeSearchText(
        [option.label, String(option.value), option.searchText ?? ''].join(' ')
      );
      return searchableText.includes(normalizedSearch);
    });
  }, [options, search]);

  const updatePosition = useCallback(() => {
    if (!triggerRef.current) return;
    const rect = triggerRef.current.getBoundingClientRect();
    const spaceBelow = window.innerHeight - rect.bottom - MENU_GAP;
    const openUp = spaceBelow < MENU_MAX_HEIGHT && rect.top > spaceBelow;
    setPosition({
      top: openUp ? rect.top - MENU_GAP : rect.bottom + MENU_GAP,
      left: rect.left,
      width: rect.width,
      openUp,
    });
  }, []);

  const open = useCallback(() => {
    if (disabled) return;
    updatePosition();
    setIsOpen(true);
    setSearch('');
    const idx = options.findIndex(o => o.value === value);
    setHighlightedIndex(idx >= 0 ? idx : 0);
  }, [disabled, updatePosition, options, value]);

  const close = useCallback(() => {
    setIsOpen(false);
    setSearch('');
    hideTooltip();
    triggerRef.current?.focus();
  }, [hideTooltip]);

  const select = useCallback(
    (option: DropdownOption<T>) => {
      onChange(option.value);
      close();
    },
    [onChange, close]
  );

  useEffect(() => {
    if (!isOpen) return;
    const handler = (e: MouseEvent) => {
      const target = e.target as Node;
      if (triggerRef.current?.contains(target) || listRef.current?.contains(target)) return;
      close();
    };
    document.addEventListener('mousedown', handler);
    return () => document.removeEventListener('mousedown', handler);
  }, [isOpen, close]);

  useEffect(() => {
    if (!isOpen) return;
    const handleResize = () => close();
    const handleScroll = (e: Event) => {
      if (listRef.current?.contains(e.target as Node)) return;
      close();
    };
    window.addEventListener('resize', handleResize);
    window.addEventListener('scroll', handleScroll, true);
    return () => {
      window.removeEventListener('resize', handleResize);
      window.removeEventListener('scroll', handleScroll, true);
    };
  }, [isOpen, close]);

  useEffect(() => {
    if (!isOpen) return;
    if (searchable) {
      requestAnimationFrame(() => searchRef.current?.focus());
    }
    requestAnimationFrame(() => {
      const list = listRef.current?.querySelector('[data-dropdown-list]');
      const selectedIdx = options.findIndex(o => o.value === value);
      if (selectedIdx >= 0 && list?.children[selectedIdx]) {
        (list.children[selectedIdx] as HTMLElement).scrollIntoView({ block: 'nearest' });
      }
    });
  }, [isOpen, searchable, options, value]);

  useEffect(() => {
    if (!isOpen || highlightedIndex < 0) return;
    const list = listRef.current?.querySelector('[data-dropdown-list]');
    const item = list?.children[highlightedIndex] as HTMLElement | undefined;
    if (typeof item?.scrollIntoView === 'function') {
      item.scrollIntoView({ block: 'nearest' });
    }
  }, [isOpen, highlightedIndex]);

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (!isOpen) {
      if (e.key === 'Enter' || e.key === ' ' || e.key === 'ArrowDown') {
        e.preventDefault();
        open();
      }
      return;
    }

    switch (e.key) {
      case 'ArrowDown':
        e.preventDefault();
        setHighlightedIndex(prev => (prev < filtered.length - 1 ? prev + 1 : 0));
        break;
      case 'ArrowUp':
        e.preventDefault();
        setHighlightedIndex(prev => (prev > 0 ? prev - 1 : filtered.length - 1));
        break;
      case 'Enter':
        e.preventDefault();
        if (highlightedIndex >= 0 && highlightedIndex < filtered.length) {
          select(filtered[highlightedIndex]);
        }
        break;
      case 'Escape':
        e.preventDefault();
        close();
        break;
      case 'Tab':
        close();
        break;
    }
  };

  const handleSearchKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp' || e.key === 'Enter' || e.key === 'Escape') {
      handleKeyDown(e);
    }
  };

  const triggerClassName = [
    'w-full flex items-center justify-between gap-2 px-3 py-2 text-sm rounded-lg border transition-colors',
    'text-content dark:text-content-dark bg-surface dark:bg-surface-dark',
    'focus:outline-none focus-visible:ring-1',
    isOpen
      ? 'border-content/20 dark:border-content-dark/20 ring-1 ring-content/10 dark:ring-content-dark/10'
      : 'border-outline/60 dark:border-outline-dark/60 focus-visible:ring-outline dark:focus-visible:ring-outline-dark focus-visible:border-outline-secondary dark:focus-visible:border-outline-accent-dark hover:bg-surface-accent dark:hover:bg-surface-accent-dark',
    'disabled:opacity-50 disabled:cursor-not-allowed',
    className,
  ].join(' ');

  const menuStyle: React.CSSProperties = {
    position: 'fixed',
    left: position.left,
    width: position.width,
    zIndex: 9999,
    ...(position.openUp ? { bottom: window.innerHeight - position.top } : { top: position.top }),
  };

  return (
    <>
      <button
        ref={triggerRef}
        type="button"
        onClick={() => (isOpen ? close() : open())}
        onKeyDown={handleKeyDown}
        disabled={disabled}
        className={triggerClassName}
        aria-haspopup="listbox"
        aria-expanded={isOpen}
        {...rest}
      >
        <span className="flex min-w-0 items-center gap-2">
          {selectedIcon && (
            <span className="text-base leading-none" aria-hidden="true">
              {selectedIcon}
            </span>
          )}
          <span className={`truncate ${selectedOption ? '' : 'text-content-muted dark:text-content-muted-dark'}`}>
            {selectedOption?.label || resolvedPlaceholder}
          </span>
        </span>
        <ChevronDown
          className={`h-4 w-4 flex-shrink-0 text-content-muted transition-transform duration-150 ${isOpen ? 'rotate-180' : ''}`}
        />
      </button>

      {isOpen &&
        createPortal(
          <div
            ref={listRef}
            role="listbox"
            style={menuStyle}
            className="rounded-lg border border-outline/60 dark:border-outline-dark/60 bg-surface dark:bg-surface-dark shadow-lg overflow-hidden"
            onKeyDown={handleKeyDown}
          >
            {searchable && (
              <div className="flex items-center gap-2 px-3 py-2 border-b border-outline/30 dark:border-outline-dark/30">
                <Search className="h-3.5 w-3.5 text-content-muted flex-shrink-0" />
                <input
                  ref={searchRef}
                  type="text"
                  value={search}
                  onChange={e => {
                    setSearch(e.target.value);
                    setHighlightedIndex(0);
                  }}
                  onKeyDown={handleSearchKeyDown}
                  placeholder={resolvedSearchPlaceholder}
                  className="flex-1 text-sm bg-transparent text-content dark:text-content-dark placeholder:text-content-muted outline-none"
                />
              </div>
            )}
            <div data-dropdown-list className="max-h-60 overflow-y-auto py-1">
              {filtered.length === 0 ? (
                <div className="px-3 py-4 text-center text-xs text-content-muted">{resolvedNoResultsText}</div>
              ) : (
                filtered.map((option, idx) => {
                  const isSelected = option.value === value;
                  const isHighlighted = idx === highlightedIndex;
                  return (
                    <button
                      key={option.value}
                      type="button"
                      role="option"
                      aria-selected={isSelected}
                      onClick={() => select(option)}
                      onMouseEnter={e => {
                        setHighlightedIndex(idx);
                        const span = e.currentTarget.querySelector('[data-truncate]') as HTMLElement | null;
                        if (span) showTooltip(option.label, span);
                      }}
                      onMouseLeave={hideTooltip}
                      className={[
                        'w-full flex items-center gap-2 px-3 py-1.5 text-sm text-left transition-colors',
                        isHighlighted ? 'bg-surface-accent dark:bg-surface-accent-dark' : '',
                        isSelected
                          ? 'text-content dark:text-content-dark font-medium'
                          : 'text-content-secondary dark:text-content-secondary-dark',
                      ].join(' ')}
                    >
                      {option.icon && (
                        <span className="text-base leading-none" aria-hidden="true">
                          {option.icon}
                        </span>
                      )}
                      <span data-truncate className="flex-1 truncate">
                        {option.label}
                      </span>
                      {isSelected && <Check className="h-3.5 w-3.5 text-primary-500 flex-shrink-0" />}
                    </button>
                  );
                })
              )}
            </div>
          </div>,
          document.body
        )}
      {isOpen &&
        tooltip &&
        createPortal(
          <div
            role="tooltip"
            className="pointer-events-none fixed z-[10000] whitespace-nowrap rounded-md bg-content dark:bg-content-dark text-button-text dark:text-button-text-dark px-2 py-1 text-xs shadow-lg"
            style={{
              left: tooltip.x,
              top: tooltip.y,
              transform: 'translate(-50%, -100%)',
            }}
          >
            {tooltip.text}
          </div>,
          document.body
        )}
    </>
  );
};
