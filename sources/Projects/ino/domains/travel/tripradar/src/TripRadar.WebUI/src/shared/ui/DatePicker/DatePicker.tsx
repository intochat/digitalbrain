import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { addDays, addMonths, format, getDaysInMonth, isSameDay, startOfMonth, subMonths } from 'date-fns';
import { enUS, ru } from 'date-fns/locale';
import { Calendar, ChevronLeft, ChevronRight } from 'lucide-react';
import { createPortal } from 'react-dom';
import { useTranslation } from 'react-i18next';
import { cn } from 'shared/lib/utils';

interface DatePickerProps {
  value: string;
  onChange: (value: string) => void;
  min?: string;
  max?: string;
  placeholder?: string;
  disabled?: boolean;
  className?: string;
  'aria-label'?: string;
}

const MENU_GAP = 4;
const MONDAY_REFERENCE_DATE = new Date(2024, 0, 1);
const DATE_LOCALES = {
  en: enUS,
  ru,
} as const;

const toDate = (v: string | undefined): Date | null => {
  if (!v) return null;
  const d = new Date(v + 'T00:00:00');
  return Number.isNaN(d.getTime()) ? null : d;
};

const toValue = (d: Date): string => format(d, 'yyyy-MM-dd');

const capitalizeFirstLetter = (value: string): string => {
  if (!value) return value;
  return value.charAt(0).toUpperCase() + value.slice(1);
};

export const DatePicker = ({
  value,
  onChange,
  min,
  max,
  placeholder,
  disabled = false,
  className,
  ...rest
}: DatePickerProps) => {
  const { i18n, t } = useTranslation();
  const locale = DATE_LOCALES[i18n.resolvedLanguage === 'ru' ? 'ru' : 'en'];
  const resolvedPlaceholder = placeholder ?? t('Select date');
  const weekdayLabels = useMemo(
    () => Array.from({ length: 7 }, (_, index) => format(addDays(MONDAY_REFERENCE_DATE, index), 'EEEEEE', { locale })),
    [locale]
  );
  const [isOpen, setIsOpen] = useState(false);
  const [viewDate, setViewDate] = useState<Date>(() => {
    const parsed = toDate(value);
    return parsed ?? new Date();
  });
  const triggerRef = useRef<HTMLButtonElement>(null);
  const panelRef = useRef<HTMLDivElement>(null);
  const [position, setPosition] = useState({ top: 0, left: 0, width: 0, openUp: false });

  const selectedDate = useMemo(() => toDate(value), [value]);
  const minDate = useMemo(() => toDate(min), [min]);
  const maxDate = useMemo(() => toDate(max), [max]);

  const updatePosition = useCallback(() => {
    if (!triggerRef.current) return;
    const rect = triggerRef.current.getBoundingClientRect();
    const spaceBelow = window.innerHeight - rect.bottom - MENU_GAP;
    const openUp = spaceBelow < 320 && rect.top > spaceBelow;
    setPosition({
      top: openUp ? rect.top - MENU_GAP : rect.bottom + MENU_GAP,
      left: rect.left,
      width: Math.max(rect.width, 280),
      openUp,
    });
  }, []);

  const open = useCallback(() => {
    if (disabled) return;
    updatePosition();
    setIsOpen(true);
    if (selectedDate) setViewDate(selectedDate);
  }, [disabled, updatePosition, selectedDate]);

  const close = useCallback(() => {
    setIsOpen(false);
    triggerRef.current?.focus();
  }, []);

  useEffect(() => {
    if (!isOpen) return;
    const handleClick = (e: MouseEvent) => {
      const target = e.target as Node;
      if (triggerRef.current?.contains(target) || panelRef.current?.contains(target)) return;
      close();
    };
    document.addEventListener('mousedown', handleClick);
    return () => document.removeEventListener('mousedown', handleClick);
  }, [isOpen, close]);

  useEffect(() => {
    if (!isOpen) return;
    const handleEsc = (e: KeyboardEvent) => {
      if (e.key === 'Escape') close();
    };
    const handleScroll = (e: Event) => {
      if (panelRef.current?.contains(e.target as Node)) return;
      close();
    };
    document.addEventListener('keydown', handleEsc);
    window.addEventListener('scroll', handleScroll, true);
    window.addEventListener('resize', close);
    return () => {
      document.removeEventListener('keydown', handleEsc);
      window.removeEventListener('scroll', handleScroll, true);
      window.removeEventListener('resize', close);
    };
  }, [isOpen, close]);

  const handleSelect = (day: Date) => {
    onChange(toValue(day));
    close();
  };

  const isDisabled = (day: Date): boolean => {
    if (minDate && day < minDate) return true;
    if (maxDate && day > maxDate) return true;
    return false;
  };

  const monthStart = startOfMonth(viewDate);
  const daysInMonth = getDaysInMonth(viewDate);
  const startDayOfWeek = (monthStart.getDay() + 6) % 7; // Monday = 0

  const days: (Date | null)[] = useMemo(() => {
    const result: (Date | null)[] = [];
    for (let i = 0; i < startDayOfWeek; i++) result.push(null);
    for (let d = 1; d <= daysInMonth; d++) {
      result.push(new Date(viewDate.getFullYear(), viewDate.getMonth(), d));
    }
    return result;
  }, [viewDate, startDayOfWeek, daysInMonth]);

  const displayValue = selectedDate ? format(selectedDate, 'PPP', { locale }) : '';

  const panelStyle: React.CSSProperties = {
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
        disabled={disabled}
        className={cn(
          'w-full flex items-center justify-between gap-2 px-3 py-2.5 text-sm rounded-lg border transition-colors',
          'text-content dark:text-content-dark bg-surface dark:bg-surface-dark',
          'focus:outline-none focus-visible:ring-1',
          'disabled:opacity-50 disabled:cursor-not-allowed',
          isOpen
            ? 'border-content/20 dark:border-content-dark/20 ring-1 ring-content/10 dark:ring-content-dark/10'
            : 'border-outline dark:border-outline-dark focus-visible:ring-outline dark:focus-visible:ring-outline-dark hover:bg-surface-accent dark:hover:bg-surface-accent-dark',
          className
        )}
        aria-haspopup="dialog"
        aria-expanded={isOpen}
        {...rest}
      >
        <span className={displayValue ? '' : 'text-content-muted dark:text-content-muted-dark'}>
          {displayValue || resolvedPlaceholder}
        </span>
        <Calendar className="h-4 w-4 flex-shrink-0 text-content-muted" />
      </button>

      {isOpen &&
        createPortal(
          <div
            ref={panelRef}
            style={panelStyle}
            className="rounded-lg border border-outline/60 dark:border-outline-dark/60 bg-surface dark:bg-surface-dark shadow-lg overflow-hidden"
          >
            <div className="flex items-center justify-between px-3 py-2.5 border-b border-outline/30 dark:border-outline-dark/30">
              <button
                type="button"
                onClick={() => setViewDate(prev => subMonths(prev, 1))}
                className="p-1 rounded-md text-content-secondary dark:text-content-secondary-dark hover:bg-surface-accent dark:hover:bg-surface-accent-dark transition-colors"
                aria-label={t('Previous month')}
              >
                <ChevronLeft className="h-4 w-4" />
              </button>
              <span className="text-sm font-medium text-content dark:text-content-dark">
                {capitalizeFirstLetter(format(viewDate, 'LLLL yyyy', { locale }))}
              </span>
              <button
                type="button"
                onClick={() => setViewDate(prev => addMonths(prev, 1))}
                className="p-1 rounded-md text-content-secondary dark:text-content-secondary-dark hover:bg-surface-accent dark:hover:bg-surface-accent-dark transition-colors"
                aria-label={t('Next month')}
              >
                <ChevronRight className="h-4 w-4" />
              </button>
            </div>

            <div className="p-3">
              <div className="grid grid-cols-7 mb-1">
                {weekdayLabels.map(day => (
                  <div
                    key={day}
                    className="text-center text-[11px] font-medium text-content-muted dark:text-content-muted-dark py-1"
                  >
                    {day}
                  </div>
                ))}
              </div>

              <div className="grid grid-cols-7">
                {days.map((day, i) => {
                  if (!day) {
                    return <div key={`empty-${i}`} />;
                  }
                  const selected = selectedDate && isSameDay(day, selectedDate);
                  const today = isSameDay(day, new Date());
                  const dayDisabled = isDisabled(day);

                  return (
                    <button
                      key={day.getTime()}
                      type="button"
                      onClick={() => !dayDisabled && handleSelect(day)}
                      disabled={dayDisabled}
                      className={cn(
                        'h-8 w-full rounded-md text-sm transition-colors',
                        dayDisabled && 'text-content-muted/30 dark:text-content-muted-dark/30 cursor-not-allowed',
                        !dayDisabled &&
                          !selected &&
                          'text-content dark:text-content-dark hover:bg-surface-accent dark:hover:bg-surface-accent-dark',
                        selected &&
                          'bg-button dark:bg-button-dark text-button-text dark:text-button-text-dark font-medium',
                        !selected && today && 'font-medium underline underline-offset-2'
                      )}
                    >
                      {day.getDate()}
                    </button>
                  );
                })}
              </div>

              <div className="mt-2 pt-2 border-t border-outline/30 dark:border-outline-dark/30 flex justify-center">
                <button
                  type="button"
                  onClick={() => {
                    const today = new Date();
                    if (!isDisabled(today)) {
                      handleSelect(today);
                    } else {
                      setViewDate(today);
                    }
                  }}
                  className="text-xs text-content-secondary dark:text-content-secondary-dark hover:text-content dark:hover:text-content-dark transition-colors"
                >
                  {t('Today')}
                </button>
              </div>
            </div>
          </div>,
          document.body
        )}
    </>
  );
};
