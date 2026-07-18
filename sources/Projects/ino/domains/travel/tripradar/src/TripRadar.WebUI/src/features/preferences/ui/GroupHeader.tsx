import React from 'react';
import { ChevronRight } from 'lucide-react';
import { useFrontendLanguage } from 'app/providers';

export interface GroupHeaderProps {
  title: string;
  isExpanded: boolean;
  onClick: () => void;
  onKeyDown?: (event: React.KeyboardEvent) => void;
  className?: string;
  id?: string;
  'aria-controls'?: string;
}

export const GroupHeader = React.memo(
  ({ title, isExpanded, onClick, onKeyDown, className = '', id, 'aria-controls': ariaControls }: GroupHeaderProps) => {
    const { t } = useFrontendLanguage();

    return (
      <button
        type="button"
        id={id}
        onClick={onClick}
        onKeyDown={onKeyDown}
        className={`
          flex items-center justify-between w-full
          min-h-[48px] py-2 px-3 text-left rounded-lg
          hover:bg-surface-accent dark:hover:bg-surface-accent-dark
          transition-colors duration-150
          focus:outline-none focus-visible:ring-1
          focus-visible:ring-outline dark:focus-visible:ring-outline-dark
          touch-manipulation
          ${className}
        `}
        aria-expanded={isExpanded}
        aria-controls={ariaControls}
        aria-describedby={`${id}-description`}
      >
        <span className="text-sm font-medium text-content dark:text-content-dark">{t(title)}</span>

        <div id={`${id}-description`} className="sr-only">
          {isExpanded
            ? t('Press Enter or Space to collapse this section')
            : t('Press Enter or Space to expand this section')}
        </div>

        <ChevronRight
          className={`
            w-4 h-4 text-content-muted dark:text-content-muted-dark flex-shrink-0
            transition-transform duration-150
            ${isExpanded ? 'rotate-90' : 'rotate-0'}
          `}
          aria-hidden="true"
        />
      </button>
    );
  }
);
