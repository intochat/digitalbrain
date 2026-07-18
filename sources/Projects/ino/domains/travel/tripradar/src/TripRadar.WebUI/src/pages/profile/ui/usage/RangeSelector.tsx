import { useFrontendLanguage } from 'app/providers';
import type { RangeOption } from './usageConstants';
import { RANGE_OPTIONS } from './usageConstants';

export interface RangeSelectorProps {
  value: RangeOption;
  onChange: (value: RangeOption) => void;
}

export const RangeSelector = ({ value, onChange }: RangeSelectorProps) => {
  const { t } = useFrontendLanguage();

  const activeClasses =
    'bg-surface dark:bg-surface-dark text-content dark:text-content-dark font-semibold shadow-sm rounded-full px-3 py-1';
  const inactiveClasses =
    'text-content-secondary dark:text-content-secondary-dark px-3 py-1 rounded-full hover:text-content dark:hover:text-content-dark';

  return (
    <div
      role="radiogroup"
      aria-label={t('Range')}
      className="inline-flex items-center rounded-full border border-outline dark:border-outline-dark bg-surface-accent/70 dark:bg-surface-accent-dark/60 p-1"
    >
      {RANGE_OPTIONS.map(option => (
        <button
          key={option}
          type="button"
          role="radio"
          aria-checked={value === option}
          onClick={() => onChange(option)}
          className={`text-sm transition-colors ${value === option ? activeClasses : inactiveClasses}`}
        >
          {t(`${option} days`)}
        </button>
      ))}
    </div>
  );
};
