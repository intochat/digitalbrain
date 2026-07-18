import { useCallback, useRef } from 'react';
import type { KeyboardEvent } from 'react';
import { useFrontendLanguage } from 'app/providers';

interface BillingToggleProps {
  isAnnual: boolean;
  onToggle: (isAnnual: boolean) => void;
  averageDiscount: number;
}

export const BillingToggle = ({ isAnnual, onToggle, averageDiscount }: BillingToggleProps) => {
  const { t } = useFrontendLanguage();
  const monthlyRef = useRef<HTMLDivElement>(null);
  const yearlyRef = useRef<HTMLDivElement>(null);

  const handleKeyDown = useCallback(
    (e: KeyboardEvent) => {
      if (e.key === 'ArrowLeft' || e.key === 'ArrowRight') {
        e.preventDefault();
        const next = !isAnnual;
        onToggle(next);
        (next ? yearlyRef : monthlyRef).current?.focus();
      }
    },
    [isAnnual, onToggle]
  );

  const activeClass = 'bg-button dark:bg-button-dark text-button-text dark:text-button-text-dark';
  const inactiveClass =
    'text-content-secondary dark:text-content-secondary-dark hover:text-content dark:hover:text-content-dark';

  const discountBadgeContent = isAnnual && averageDiscount > 0 && (
    <span className="inline-flex items-center rounded-full bg-secondary-50 dark:bg-secondary-500/15 border border-secondary-500/20 dark:border-secondary-400/25 text-secondary-700 dark:text-secondary-300 text-xs font-semibold px-3 py-1">
      {t('Save {averageDiscount}%', { averageDiscount })}
    </span>
  );

  return (
    <div className="relative flex flex-col items-center">
      <div
        role="radiogroup"
        aria-label={t('Billing period')}
        className="inline-flex items-center rounded-full border border-outline dark:border-outline-dark bg-surface dark:bg-surface-dark p-1"
        onKeyDown={handleKeyDown}
      >
        <div
          ref={monthlyRef}
          role="radio"
          aria-checked={!isAnnual}
          tabIndex={!isAnnual ? 0 : -1}
          onClick={() => onToggle(false)}
          className={`cursor-pointer rounded-full px-5 py-2 text-sm font-medium transition-colors ${!isAnnual ? activeClass : inactiveClass}`}
        >
          {t('Monthly')}
        </div>
        <div
          ref={yearlyRef}
          role="radio"
          aria-checked={isAnnual}
          tabIndex={isAnnual ? 0 : -1}
          onClick={() => onToggle(true)}
          className={`cursor-pointer rounded-full px-5 py-2 text-sm font-medium transition-colors ${isAnnual ? activeClass : inactiveClass}`}
        >
          {t('Yearly')}
        </div>
      </div>
      {discountBadgeContent && (
        <div className="mt-3 sm:absolute sm:mt-0 sm:top-1/2 sm:-translate-y-1/2 sm:left-full sm:ml-4 whitespace-nowrap">
          {discountBadgeContent}
        </div>
      )}
    </div>
  );
};
